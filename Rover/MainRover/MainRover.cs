﻿using System;
using Scarlet;
using Scarlet.IO.BeagleBone;
using Scarlet.IO;
using System.Collections.Generic;
using Scarlet.Components;
using Scarlet.Components.Sensors;
using Scarlet.Communications;
using Scarlet.Utilities;
using System.Threading;

namespace MainRover
{
    public class MainRover
    {
        public static string SERVER_IP = "192.168.0.5";
        public const int NUM_PACKETS_TO_PROCESS = 20;

        public static bool Quit;
        public static List<ISensor> Sensors;
        public static QueueBuffer StopPackets;
        public static QueueBuffer ModePackets;
        public static QueueBuffer DrivePackets;
        public static QueueBuffer PathPackets;
        public static IPWMOutput OutA;
        public static IPWMOutput OutB;

        public enum DriveMode {BaseDrive, toGPS, findTennisBall, toTennisBall, destination};
        public static DriveMode CurDriveMode;
        public static Tuple<float, float> previousCoords;
        public static float PathSpeed, PathAngle;
        public static float ServoSpinner; 

        public static void PinConfig()
        {
            BBBPinManager.AddBusCAN(0);
            //BBBPinManager.AddMappingUART(Pins.MTK3339_RX);
            //BBBPinManager.AddMappingUART(Pins.MTK3339_TX);
            //BBBPinManager.AddMappingsI2C(Pins.BNO055_SCL, Pins.BNO055_SDA);
            //BBBPinManager.AddMappingGPIO(Pins.SteeringLimitSwitch, false, Scarlet.IO.ResistorState.PULL_UP, true);
            //BBBPinManager.AddMappingPWM(Pins.SteeringMotor);
            //BBBPinManager.AddMappingPWM(Pins.ServoMotor);
            BBBPinManager.AddMappingPWM(Pins.CameraServoMotor);
            //BBBPinManager.ApplyPinSettings(BBBPinManager.ApplicationMode.NO_CHANGES);
            BBBPinManager.ApplyPinSettings(BBBPinManager.ApplicationMode.APPLY_IF_NONE);
        }

        public static void IPWMOutputConfig()
        {
            OutA = PWMBBB.PWMDevice1.OutputA;
            OutA.SetFrequency(50);
            OutA.SetOutput(0.0f);
            OutA.SetEnabled(true);
            OutB = PWMBBB.PWMDevice1.OutputB;
            OutB.SetFrequency(50);
            OutB.SetOutput(0.0f);
            OutB.SetEnabled(true);
            ServoSpinner = 0.05f;
        }

        public static void InitBeagleBone()
        {
            StateStore.Start("MainRover");
            BeagleBone.Initialize(SystemMode.NO_HDMI, true);
            PinConfig();
            IPWMOutputConfig();
            Sensors = new List<ISensor>();
            try
            {
                //Sensors.Add(new BNO055(I2CBBB.I2CBus2));
                //Sensors.Add(new MTK3339(UARTBBB.UARTBus4));
            }
            catch (Exception e)
            {
                Log.Output(Log.Severity.ERROR, Log.Source.SENSORS, "Failed to initalize sensors (gps and/or mag)");
                Log.Exception(Log.Source.SENSORS, e);
            }

            foreach (ISensor Sensor in Sensors)
            {
                if (Sensor is MTK3339)
                {
                    previousCoords = ((MTK3339)Sensor).GetCoordinates();
                }
            }
        }

        public static void SetupClient()
        {
            Client.Start(SERVER_IP, 1025, 1026, "MainRover");
            DrivePackets = new QueueBuffer();
            StopPackets = new QueueBuffer();
            ModePackets = new QueueBuffer();
            PathPackets = new QueueBuffer();
            Parse.SetParseHandler(0x80, (Packet) => StopPackets.Enqueue(Packet, 0));
            Parse.SetParseHandler(0x99, (Packet) => ModePackets.Enqueue(Packet, 0));
            for (byte i = 0x8E; i <= 0x94; i++)
                Parse.SetParseHandler(i, (Packet) => DrivePackets.Enqueue(Packet, 0));
            Parse.SetParseHandler(0x98, (Packet) => DrivePackets.Enqueue(Packet, 0));
            for (byte i = 0x9A; i <= 0xA1; i++)
                Parse.SetParseHandler(i, (Packet) => DrivePackets.Enqueue(Packet, 0));
            for (byte i = 0x95; i <= 0x97; i++)
                Parse.SetParseHandler(i, (Packet) => PathPackets.Enqueue(Packet, 0));
            PathSpeed = 0;
            PathAngle = 0;
        }

        public static void ProcessInstructions()
        {
            if (!StopPackets.IsEmpty())
            {
                StopPackets = new QueueBuffer();
                // Stop the Rover

            }
            else if (!ModePackets.IsEmpty())
            {
                ProcessModePackets();
            }
            else
            {
                switch (CurDriveMode)
                {   // TODO For each case statement, clear undeeded queue buffers
                    case DriveMode.BaseDrive:
                        ProcessBasePackets();
                        PathPackets = new QueueBuffer();
                        break;
                    case DriveMode.toGPS:
                        ProcessPathPackets();
                        DrivePackets = new QueueBuffer();
                        break;
                    case DriveMode.findTennisBall:
                        DrivePackets = new QueueBuffer();
                        PathPackets = new QueueBuffer();
                        break;
                    case DriveMode.toTennisBall:
                        DrivePackets = new QueueBuffer();
                        PathPackets = new QueueBuffer();
                        break;
                    case DriveMode.destination:
                        DrivePackets = new QueueBuffer();
                        PathPackets = new QueueBuffer();
                        //Spinning Motor
                        float t = 0.1f;
                        while (t < .9f)
                        {
                            OutA.SetOutput(t);
                            t += 0.0001f;
                        }
                        while (t > .1f)
                        {
                            OutA.SetOutput(t);
                            t -= 0.0001f;
                        }
                        OutA.Dispose();
                        break;
                }
            }
        }

        public static void ProcessModePackets()
        {
            for (int i = 0; !ModePackets.IsEmpty() && i < NUM_PACKETS_TO_PROCESS; i++)
            {
                Packet p = ModePackets.Dequeue();                
                CurDriveMode = (DriveMode)p.Data.Payload[1];             
            }
        }

        public static void ProcessBasePackets()
        {
            
            for (int i = 0; !DrivePackets.IsEmpty() && i < NUM_PACKETS_TO_PROCESS; i++)
            {
                Console.WriteLine("Processing Base Packets");
                Packet p = DrivePackets.Dequeue();
                switch ((PacketID)p.Data.ID)
                {   
                    //case PacketID.RPMAllDriveMotors:
                    //    MotorControl.SetAllRPM((sbyte)p.Data.Payload[0]);
                    //    break;
                    case PacketID.RPMFrontRight:
                    case PacketID.RPMFrontLeft:
                    case PacketID.RPMBackRight:
                    case PacketID.RPMBackLeft:
                        int MotorID = p.Data.ID - (byte)PacketID.RPMFrontRight;
                        MotorControl.SetRPM(MotorID, (sbyte)p.Data.Payload[1]);
                        break;
                    case PacketID.SpeedAllDriveMotors:
                        float Speed = UtilData.ToFloat(p.Data.Payload);
                        MotorControl.SetAllSpeed(Speed);
                        break;
                    case PacketID.BaseSpeed:
                    case PacketID.ShoulderSpeed:
                    case PacketID.ElbowSpeed:
                    case PacketID.WristSpeed:
                    case PacketID.DifferentialVert:
                    case PacketID.DifferentialRotate:
                    case PacketID.HandGrip:
                        byte address = (byte)(p.Data.ID - 0x8A);
                        byte direction = 0x00;
                        if (p.Data.Payload[0] > 0)
                        {
                            direction = 0x01;
                            //UtilCan.SpeedDir(CANBBB.CANBus0, false, 2, address, (byte)(-p.Data.Payload[1]), direction);
                            Console.WriteLine("ADDRESS :" + address + "DIR :" + direction + "PAY :" + (byte)(-p.Data.Payload[1]));
                        }
                        else
                        {
                            direction = 0x00;
                            //UtilCan.SpeedDir(CANBBB.CANBus0, false, 2, address, p.Data.Payload[1], direction);
                            Console.WriteLine("ADDRESS :" + address + "DIR :" + direction + "PAY :" + p.Data.Payload[1]);
                        }
                        break;
                    case PacketID.CameraRotation:
                        if((sbyte)p.Data.Payload[1] > 0)
                        {
                            ServoSpinner -= 0.005f;
                            OutB.SetOutput(ServoSpinner);
                        }
                        else if((sbyte)p.Data.Payload[1] < 0)
                        {
                            ServoSpinner += 0.005f;
                            OutB.SetOutput(ServoSpinner);
                        }
                        OutB.Dispose();
                        break;
                }
            }
        }

        public static void ProcessPathPackets()
        {
            for (int i = 0; !PathPackets.IsEmpty() && i < NUM_PACKETS_TO_PROCESS; i++)
            {
                Packet p = PathPackets.Dequeue();
                switch ((PacketID)p.Data.ID)
                {
                    // TODO Maybe: Combine pathing speed and turn in same packet???
                    case PacketID.PathingSpeed:
                        PathSpeed = UtilData.ToFloat(p.Data.Payload);
                        MotorControl.SkidSteerDriveSpeed(PathSpeed, PathAngle);
                        break;
                    case PacketID.PathingTurnAngle:
                        PathAngle = UtilData.ToFloat(p.Data.Payload);
                        MotorControl.SkidSteerDriveSpeed(PathSpeed, PathAngle);
                        break;
                }
            }
        }

        public static void SendSensorData(int count)
        {
            foreach (ISensor Sensor in Sensors)
            {
                if (Sensor is MTK3339)
                {
                    var Tup = ((MTK3339)Sensor).GetCoordinates();
                    float Lat = Tup.Item1;
                    float Long = Tup.Item2;
                    Packet Pack = new Packet((byte)PacketID.DataGPS, true);
                    Pack.AppendData(UtilData.ToBytes(Lat));
                    Pack.AppendData(UtilData.ToBytes(Long));
                    Client.SendNow(Pack);
                    if(count == 100)
                    {
                        Packet HeadingFromGPSPack = new Packet((byte)PacketID.HeadingFromGPS, true);
                        //Math between two coords given from Tup and previousCoords
                        float latDiff = Lat - previousCoords.Item1;
                        float longDiff = Long - previousCoords.Item2;
                        float theta = (float)Math.Atan2(latDiff, longDiff);
                        if (longDiff > 0)
                        {
                            theta = 90 - theta;
                        }
                        else if(longDiff < 0)
                        {
                            theta = 270 - theta;
                        }
                        HeadingFromGPSPack.AppendData(UtilData.ToBytes(theta));
                        Client.SendNow(HeadingFromGPSPack);
                        previousCoords = Tup;
                    }
                }
                if (Sensor is BNO055)
                {
                    var Tup = ((BNO055)Sensor).GetVector(BNO055.VectorType.VECTOR_MAGNETOMETER);
                    float X = Tup.Item1;
                    float Y = Tup.Item2;
                    float Z = Tup.Item3;
                    Packet Pack = new Packet((byte)PacketID.DataMagnetometer, true);
                    Pack.AppendData(UtilData.ToBytes(X));
                    Pack.AppendData(UtilData.ToBytes(Y));
                    Pack.AppendData(UtilData.ToBytes(Z));
                    Client.SendNow(Pack);
                }
            }
        }

        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                SERVER_IP = args[0];
                Console.WriteLine("Using argument Specified IP of: " + SERVER_IP);
            }
            else
            {
                Console.WriteLine("Using default IP of: " + SERVER_IP);
            }
            Quit = false;
            InitBeagleBone();
            SetupClient();
            MotorControl.Initialize();
            //MotorBoards.Initialize(CANBBB.CANBus0);
            int count = 0;
            Console.WriteLine("Finished the initalize");
            do
            {
                Console.WriteLine("Looping");
                SendSensorData(count);
                ProcessInstructions();
                Thread.Sleep(50);
                count++;
                if(count == 101)
                {
                    count = 0;
                }
            } while (!Quit);
        }
    }
}
