﻿using System;
using System.Threading;
using Scarlet.Components;
using Scarlet.Components.Interfaces;
using Scarlet.Components.Outputs;
using Scarlet.IO;
using Scarlet.IO.RaspberryPi;
using Scarlet.IO.Transforms;
using Scarlet.Utilities;
using Science.Systems;

namespace Science
{
    class IOHandler
    {
        private readonly ISubsystem[] InitProcedure;
        private readonly ISubsystem[] EStopProcedure;
        private readonly ISubsystem[] UpdateProcedure;

        //public readonly Rail RailController;
        public readonly Drill DrillController;
        //public readonly Sample SampleController;
        public readonly LEDs LEDController;
        //public readonly AuxSensors AuxSensors;
        public readonly SysSensors SysSensors;
        public readonly MusicPlayer Music;

        private readonly II2CBus I2C;
        private readonly ISPIBus SPI;
        private readonly PCA9685 PWMGenServo;
        private readonly PCA9535E IOExpander;
        private readonly MAX571x DAC;

        public IOHandler()
        {
            RaspberryPi.Initialize();
            this.I2C = new I2CBusPi();
            this.SPI = new SPIBusPi(0);
            this.PWMGenServo = new PCA9685(this.I2C, 0x74, -1, PCA9685.OutputInvert.Inverted, PCA9685.OutputDriverMode.OpenDrain, PCA9685.OutputDisableBehaviour.HighImpedance);
            //this.PWMGenServo.TraceLogging = true;
            this.PWMGenServo.SetFrequency(50);

            IDigitalOut EnableMotors = new DigitalOutPi(33);
            IDigitalIn MotorFault = new DigitalInPi(7);

            this.IOExpander = new PCA9535E(this.I2C, 0x27);
            for (byte i = 0; i < 16; i++) { this.IOExpander.SetChannelMode(i, true); } // Set all channels to output mode.
            IDigitalOut EnablePWM = this.IOExpander.Outputs[12];
            IDigitalOut DirectionDrill = this.IOExpander.Outputs[8];
            EnablePWM.SetOutput(false); // Inverted
            IDigitalOut CS_DAC = this.IOExpander.Outputs[4];
            CS_DAC.SetOutput(true);

            this.DAC = new MAX571x(this.SPI, CS_DAC, MAX571x.Resolution.BitCount12, MAX571x.VoltageReferenceMode.Reference2V500);
            this.DAC.TraceLogging = true;
            IAnalogueOut DrillMotorDAC = this.DAC.Outputs[0];

            AnalogueOutTransform DrillTransform = new AnalogueOutTransform(DrillMotorDAC, (range => (range / 2.5)), (val => (val * 2.5)));

            LTC6992 DrillMotorPWM = new LTC6992(DrillTransform);

            EnableMotors.SetOutput(true);

            //this.RailController = new Rail(this.PWMGenHighFreq.Outputs[0], new DigitalInPi(11), this.SPI, new DigitalOutPi(29), this.I2C, null) { TraceLogging = true };
            this.DrillController = new Drill(DrillMotorPWM, DirectionDrill, MotorFault, this.PWMGenServo.Outputs[4]);
            //this.SampleController = new Sample(this.PWMGenLowFreq.Outputs[1]);
            this.LEDController = new LEDs(this.PWMGenServo.Outputs, null);//EnablePWM);
            //this.LEDController.TraceLogging = true;
            //this.AuxSensors = new AuxSensors(this.SPI, this.I2C) { TraceLogging = false };
            this.SysSensors = new SysSensors(this.I2C, this.SPI);
            //this.SysSensors.TraceLogging = true; // TODO: Turn this off.
            this.Music = new MusicPlayer();

            this.InitProcedure = new ISubsystem[] { /*this.RailController, */this.DrillController, this.LEDController, /*this.AuxSensors,*/ this.SysSensors, this.Music };
            this.EStopProcedure = new ISubsystem[] { this.Music, /*this.RailController,*/ this.DrillController, this.LEDController, /*this.AuxSensors,*/ this.SysSensors };
            this.UpdateProcedure = new ISubsystem[] { /*this.RailController,*/ this.DrillController, this.LEDController/*, this.AuxSensors, this.SysSensors*/ };
            if (this.EStopProcedure.Length < this.InitProcedure.Length || this.EStopProcedure.Length < this.UpdateProcedure.Length) { throw new Exception("A system is registered for init or updates, but not for emergency stop. For safety reasons, this is not permitted."); }
        }

        /// <summary> Prepares all systems for use by zeroing them. This takes a while. </summary>
        public void InitializeSystems()
        {
            for(int i = 0; i < this.InitProcedure.Length; i++)
            {
                Log.Output(Log.Severity.DEBUG, Log.Source.SUBSYSTEM, "Initializing system #" + i + ".");
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
                Log.Output(Log.Severity.DEBUG, Log.Source.SUBSYSTEM, "E-Stopping system #" + i + ".");
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

        public void Exit()
        {
            for (int i = this.InitProcedure.Length - 1; i >= 0; i--)
            {
                Log.Output(Log.Severity.DEBUG, Log.Source.SUBSYSTEM, "Exiting system #" + i + ".");
                try { this.InitProcedure[i].Exit(); }
                catch (Exception Exc)
                {
                    Log.Output(Log.Severity.WARNING, Log.Source.SUBSYSTEM, "Failed to exit for system #" + i + ".");
                    Log.Exception(Log.Source.SUBSYSTEM, Exc);
                }
            }
        }
    }
}
