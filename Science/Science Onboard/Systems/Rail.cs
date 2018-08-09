﻿using System;
using System.Linq;
using System.Threading;
using System.Timers;
using Scarlet.Communications;
using Scarlet.Components;
using Scarlet.Components.Motors;
using Scarlet.Components.Outputs;
using Scarlet.Components.Sensors;
using Scarlet.Filters;
using Scarlet.IO;
using Scarlet.IO.Utilities;
using Scarlet.Utilities;
using Science.Library;

namespace Science.Systems
{
    public class Rail : ISubsystem
    {
        public bool TraceLogging { get; set; }

        private const float MOTOR_MAX_SPEED = 0.5F;
        private const int INIT_TIMEOUT = 5000;
        private const float ENCODER_MM_PER_TICK = 0.935F; // TODO: Placeholder value. Replace.
        private const bool ENABLE_VELOCITY_TRACKING = true;

        private bool P_Initializing = false;
        private bool Initializing
        {
            get => this.P_Initializing;
            set
            {
                this.P_Initializing = value;
                this.LED?.InitStateChange(value);
            }
        }
        private bool InitDone = false; // Whether the rail has initialized successfully (i.e. knows how far away from the top it is).
        private double TopDepth; // The distance that the top of the rail is away from the very top position (limit switch), in mm.
        private Average<double> GroundHeightFilter; // Filter used on measurements meaning distance that the bottom of the drill is away from the ground, in mm (below ground is negative).
        private int LastEncoderCount; // Where the encoder was during the most recent update.
        private Average<double> VelocityTracker; // Used to detect faulty hardware or stalls by comparing motor output speed with actual movement.

        public bool TargetLocationRefIsTop = true; // The following target distance is from the top of the rail (true) or the ground (false).
        public double TargetLocation; // Where the operator would like the rail to go.

        public float RailSpeed = 0.3F; // The speed that the rail should move at when applicable.

        private readonly TalonMC MotorCtrl;
        private readonly LimitSwitch Limit;
        private readonly LS7366R Encoder;
        private readonly VL53L0X_MVP Ranger;

        private readonly LEDController LED;

        /// <summary> Handles moving the linear rail up and down for the various experiments. </summary>
        public Rail(IPWMOutput MotorPWM, IDigitalIn LimitSw, ISPIBus EncoderSPI, IDigitalOut EncoderCS, II2CBus RangerBus, RGBLED LED)
        {
            this.MotorCtrl = new TalonMC(MotorPWM, MOTOR_MAX_SPEED);
            IDigitalIn FakeInterrupt = new SoftwareInterrupt(LimitSw) { TraceLogging = true };
            
            this.Limit = new LimitSwitch(FakeInterrupt, false);
            this.Encoder = new LS7366R(EncoderSPI, EncoderCS); // Note: Count goes down when the rail moves down.
            LS7366R.Configuration Config = LS7366R.DefaultConfig;
            Config.QuadMode = LS7366R.QuadMode.X4_QUAD;
            this.Encoder.Configure(Config);
            this.GroundHeightFilter = new Average<double>(4);
            this.VelocityTracker = new Average<double>(10);
            this.Ranger = new VL53L0X_MVP(RangerBus);
            
            this.LED = new LEDController(LED);
        }

        private void EventTriggered(object Sender, EventArgs Event)
        {
            if(Event is LimitSwitchToggle && this.Initializing) // We hit the end.
            {
                this.MotorCtrl.SetSpeed(0);
                this.TopDepth = 0;
                Log.Output(Log.Severity.DEBUG, Log.Source.MOTORS, "Rail motor finished initializing.");
                this.Initializing = false;
                this.InitDone = true;
            }
            else if(Event is ElapsedEventArgs && this.Initializing) // We timed out trying to initialize.
            {
                this.MotorCtrl.SetSpeed(0);
                Log.Output(Log.Severity.ERROR, Log.Source.MOTORS, "Rail motor timed out while trying to initialize.");
                this.Initializing = false;
                this.InitDone = false;
            }
            else if(Event is LimitSwitchToggle && ((LimitSwitchToggle)Event).CurrentState && !this.Initializing) // We hit the end during operation.
            {
                this.MotorCtrl.SetEnabled(false); // Immediately stop.
                this.TopDepth = 0;
                this.TargetLocation = 0;
                this.TargetLocationRefIsTop = true;
                Log.Output(Log.Severity.WARNING, Log.Source.MOTORS, "Rail motor hit limit switch and was stopped for safety.");
            }
        }

        /// <summary> Prepares the rail for use by moving the motor all the way up to the top to find the zero position. </summary>
        public void Initialize()
        {
            this.Initializing = true;
            this.Limit.SwitchToggle += this.EventTriggered;

            System.Timers.Timer TimeoutTrigger = new System.Timers.Timer() { Interval = INIT_TIMEOUT, AutoReset = false };
            TimeoutTrigger.Elapsed += this.EventTriggered;
            TimeoutTrigger.Enabled = true;
            this.Encoder.UpdateState();
            this.LastEncoderCount = this.Encoder.Count;
            if (this.Limit.State) // We are already at the top, nothing needs to be done.
            {
                Log.Output(Log.Severity.INFO, Log.Source.MOTORS, "Rail already at top. No need for init.");
                TimeoutTrigger.Enabled = false;
                this.InitDone = true;
                this.Initializing = false;
                this.TopDepth = 0;
                return;
            }
            this.MotorCtrl.SetSpeed(this.RailSpeed);
            this.MotorCtrl.SetEnabled(true);
            // Either the limit switch will be toggled, or the timeout event will happen after this.
        }

        /// <summary> Moves the rail to the highest position. </summary>
        public void GotoTop()
        {
            if (!this.InitDone) { Log.Output(Log.Severity.ERROR, Log.Source.MOTORS, "Tried to move rail to top without having done init."); return; }
            this.TargetLocation = 0;
            this.TargetLocationRefIsTop = true;
        }

        /// <summary> Move the rail to where the drill is just above the ground. </summary>
        public void GotoDrillGround()
        {
            if (!this.InitDone) { Log.Output(Log.Severity.ERROR, Log.Source.MOTORS, "Tried to move rail to ground without having done init."); return; }
            this.TargetLocation = 200;
            this.TargetLocationRefIsTop = false;
        }

        /// <summary> Immediately stops the rail motor. </summary>
        public void EmergencyStop()
        {
            this.TargetLocation = this.TopDepth;
            this.MotorCtrl.SetEnabled(false);
            UpdateState();
            this.MotorCtrl.SetEnabled(false);
        }

        /// <summary>
        /// Updates the current position, target, intended motor speed. Communicates this to the Talon controller to drive the motor at the new desired rate.
        /// </summary>
        public void UpdateState()
        {
            this.Limit.UpdateState();
            this.Encoder.UpdateState();
            this.TopDepth += ((this.LastEncoderCount - this.Encoder.Count) * ENCODER_MM_PER_TICK);
            this.LastEncoderCount = this.Encoder.Count;

            try
            {
                byte[] Data = new byte[] { (byte)((this.InitDone ? 0b1 : 0b0) | (this.Initializing ? 0b10 : 0b00) | (this.TargetLocationRefIsTop ? 0b100 : 0b000)) } // Basic Data
                    .Concat(UtilData.ToBytes(this.RailSpeed)) // Rail Speed
                    .Concat(UtilData.ToBytes((float)this.TopDepth)) // Depth from top
                    .Concat(UtilData.ToBytes((float)this.GroundHeightFilter.GetOutput())) // Height from GND
                    .Concat(UtilData.ToBytes((float)this.TargetLocation)) // Target depth
                    .ToArray();
                Packet Packet = new Packet(new Message(ScienceConstants.Packets.RAIL_STATUS, Data), false);
                Client.Send(Packet);
            }
            catch(Exception Exc)
            {
                Log.Output(Log.Severity.WARNING, Log.Source.NETWORK, "Failed to send rail status packet.");
                Log.Exception(Log.Source.NETWORK, Exc);
            }

            this.Ranger.UpdateState();
            if (this.TraceLogging) { Log.Trace(this, "Ranger seeing " + this.Ranger.GetDistance() + "mm."); }
            uint GroundDist = this.Ranger.GetDistance();
            if (GroundDist == 0 || this.Ranger.LastHadTimeout()) { Log.Output(Log.Severity.INFO, Log.Source.SENSORS, "VL53L0X did not return a valid distance."); }
            else { this.GroundHeightFilter.Feed(GroundDist - 110); } // TODO: Measure distance sensor to drill tip distance.

            if (!this.InitDone) { return; } // Don't try to move the rail if we don't know where we are.

            if (this.TraceLogging) { Log.Trace(this, "Rail at " + this.TopDepth.ToString("F2") + "mm from top, and wants to be at " + this.TargetLocation.ToString("F2") + "mm from " + (this.TargetLocationRefIsTop ? "top" : "bottom") + "."); }

            float TargetSpeed;
            if (this.TargetLocationRefIsTop && (Math.Abs(this.TargetLocation - this.TopDepth) > 5)) // The rail needs to be moved.
            {
                if (this.TraceLogging) { Log.Trace(this, "Moving at " + (this.RailSpeed * (((this.TargetLocation - this.TopDepth) > 0) ? -1 : 1))); }
                TargetSpeed = this.RailSpeed * (((this.TargetLocation - this.TopDepth) > 0) ? -1 : 1);
            }
            else { TargetSpeed = 0; }

            // Now we know our intentions, check if there is anything that should stop movement.
            if (this.TopDepth > 500) { TargetSpeed = 0; } // TODO: Verify safe maximum extension of the rail.
            if (this.GroundHeightFilter.GetOutput() < -110) { TargetSpeed = 0; } // TODO: Verify safe maximum drill depth.

            this.MotorCtrl.SetSpeed(TargetSpeed);
        }

        public void Exit()
        {
            this.TargetLocation = this.TopDepth;
            this.MotorCtrl.SetEnabled(false);
        }

        private class LEDController
        {
            private RGBLED LED;
            private int Blinking = -1; // -1 for no, otherwise colour.

            public LEDController(RGBLED LED)
            {
                this.LED = LED;
                new Thread(new ThreadStart(this.DoUpdates)).Start();
            }

            public void UnexpectedLimit()
            {
                
            }

            public void InitStateChange(bool NewState)
            {
                if(!NewState) // Initialization stopped
                {
                    //
                }
            }

            // Run on a separate thread to keep LED updates from slowing down Rail controls.
            private void DoUpdates()
            {

            }
        }
    }
}
