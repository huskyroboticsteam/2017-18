﻿using System;
using Scarlet.Components;
using Scarlet.Components.Outputs;
using Scarlet.IO;
using Scarlet.IO.RaspberryPi;
using Scarlet.Utilities;
using Science.Systems;

namespace Science
{
    class IOHandler
    {
        private readonly ISubsystem[] InitProcedure;
        private readonly ISubsystem[] EStopProcedure;
        private readonly ISubsystem[] UpdateProcedure;

        public readonly Rail RailController;
        public readonly Drill DrillController;
        public readonly Sample SampleController;
        public readonly LEDs LEDController;
        public readonly AuxSensors AuxSensors;
        public readonly SysSensors SysSensors;
        public readonly MusicPlayer Music;

        private II2CBus I2C;
        private PCA9685 PWMGenLowFreq, PWMGenHighFreq;

        public IOHandler()
        {
            RaspberryPi.Initialize();
            this.I2C = new I2CBusPi();
            this.PWMGenHighFreq = new PCA9685(this.I2C, 0x4C, -1, PCA9685.OutputInvert.Inverted, PCA9685.OutputDriverMode.OpenDrain) { TraceLogging = true };
            this.PWMGenLowFreq = new PCA9685(this.I2C, 0x74, -1, PCA9685.OutputInvert.Inverted, PCA9685.OutputDriverMode.OpenDrain) { TraceLogging = true };
            this.PWMGenHighFreq.SetFrequency(333);
            this.PWMGenLowFreq.SetFrequency(50);

            //TODO: Is it OK to construct a comms bus more than once for the different systems? Answer: Threading issues!
            //this.RailController = new Rail(this.PWMGenHighFreq.Outputs[1], new DigitalInPi(11), new SPIBusPi(0), new DigitalOutPi(31), new I2CBusPi(), null);
            this.DrillController = new Drill(this.PWMGenHighFreq.Outputs[0], this.PWMGenLowFreq.Outputs[0]);
            this.SampleController = new Sample(this.PWMGenLowFreq.Outputs[1]);
            this.LEDController = new LEDs(this.PWMGenLowFreq.Outputs, this.PWMGenHighFreq.Outputs);
            this.AuxSensors = new AuxSensors() { TraceLogging = true };
            this.SysSensors = new SysSensors() { TraceLogging = true };
            this.Music = new MusicPlayer();

            this.InitProcedure = new ISubsystem[] { /*this.RailController,*/ this.DrillController, /*this.SampleController, */this.LEDController, this.AuxSensors, this.SysSensors, this.Music };
            this.EStopProcedure = new ISubsystem[] { this.Music, /*this.RailController,*/ this.DrillController, this.SampleController, this.LEDController, this.AuxSensors, this.SysSensors };
            this.UpdateProcedure = new ISubsystem[] { /*this.RailController,*/ this.DrillController, /*this.SampleController, */this.LEDController/*, this.AuxSensors, this.SysSensors*/ };
            if (this.EStopProcedure.Length < this.InitProcedure.Length || this.EStopProcedure.Length < this.UpdateProcedure.Length) { throw new Exception("A system is registered for init or updates, but not for emergency stop. For safety reasons, this is not permitted."); }
        }

        /// <summary> Prepares all systems for use by zeroing them. This takes a while. </summary>
        public void InitializeSystems()
        {
            for(int i = 0; i < this.InitProcedure.Length; i++)
            {
                try { this.InitProcedure[i].Initialize(); }
                catch(Exception Exc)
                {
                    Log.Output(Log.Severity.ERROR, Log.Source.SUBSYSTEM, "Failed to initialize system #" + i + ".");
                    Log.Exception(Log.Source.SUBSYSTEM, Exc);
                }
            }
        }

        /// <summary> Immediately stops all systems. </summary>
        public void EmergencyStop()
        {
            for (int i = 0; i < this.EStopProcedure.Length; i++)
            {
                try { this.EStopProcedure[i].EmergencyStop(); }
                catch (Exception Exc)
                {
                    Log.Output(Log.Severity.FATAL, Log.Source.SUBSYSTEM, "Failed to e-stop system #" + i + ".");
                    Log.Exception(Log.Source.SUBSYSTEM, Exc);
                }
            }
        }

        public void UpdateStates()
        {
            for (int i = 0; i < this.UpdateProcedure.Length; i++)
            {
                try { this.UpdateProcedure[i].UpdateState(); }
                catch (Exception Exc)
                {
                    Log.Output(Log.Severity.WARNING, Log.Source.SUBSYSTEM, "Failed to update state for system #" + i + ".");
                    Log.Exception(Log.Source.SUBSYSTEM, Exc);
                }
            }
        }
    }
}
