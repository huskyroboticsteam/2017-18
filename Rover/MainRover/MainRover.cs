﻿using System;
using System.Net;
using Scarlet;
using Scarlet.IO.BeagleBone;
using Scarlet.IO;
using System.Collections.Generic;
using Scarlet.Components;
using Scarlet.Components.Sensors;
using Scarlet.Communications;
using Scarlet.Utilities;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using Scarlet.Filters;

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
        public static IPWMOutput ArrivalServo;
        public static IPWMOutput CamereaServo;

        public enum DriveMode { BaseDrive, toGPS, findTennisBall, toTennisBall, destination };
        public static DriveMode CurDriveMode;
        public static Tuple<float, float> previousCoords;
        public static float PathSpeed, PathAngle;
        public static float ServoSpinner; 

        public static UdpClient udpServer;
        public static IPEndPoint remoteEP;
        public static UdpClient client;
        public static IPEndPoint ep;
        public static Queue<byte[]> recieveList;
        private static Thread ParseThread;
        private static int timeout;
        private static int GSpeed;
        private static int Gturn;
        private static Average<double> MagFilter;
        private static int servoTurn;
        private static Thread servoThread;


        public static void PinConfig()
        {
            BBBPinManager.AddBusCAN(0);
            BBBPinManager.AddMappingUART(Pins.MTK3339_RX);
            BBBPinManager.AddMappingUART(Pins.MTK3339_TX);
            BBBPinManager.AddMappingsI2C(Pins.BNO055_SCL, Pins.BNO055_SDA);
            //BBBPinManager.AddMappingGPIO(Pins.SteeringLimitSwitch, false, Scarlet.IO.ResistorState.PULL_UP, true);
            //BBBPinManager.AddMappingPWM(Pins.SteeringMotor);
            //BBBPinManager.AddMappingPWM(Pins.ServoMotor);
            BBBPinManager.AddMappingPWM(Pins.CameraServoMotor);
            //BBBPinManager.ApplyPinSettings(BBBPinManager.ApplicationMode.NO_CHANGES);
            BBBPinManager.ApplyPinSettings(BBBPinManager.ApplicationMode.APPLY_IF_NONE);
        }

        public static void IPWMOutputConfig()
        {
            ArrivalServo = PWMBBB.PWMDevice1.OutputA;
            ArrivalServo.SetFrequency(50);
            ArrivalServo.SetOutput(0.0f); // Zero means stop at 50 freq
            ArrivalServo.SetEnabled(true);
            CamereaServo = PWMBBB.PWMDevice1.OutputB;
            CamereaServo.SetFrequency(500);
            CamereaServo.SetOutput(1f); // One is stop at 500 freq
            CamereaServo.SetEnabled(true);
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
                MTK3339 location = new MTK3339(UARTBBB.UARTBus4);
                Sensors.Add(new BNO055(I2CBBB.I2CBus1, -1, 0x28, location) { TraceLogging = true });
                Sensors.Add(location);
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
                if (Sensor is BNO055)
                {
                    ((BNO055)Sensor).SetMode(BNO055.OperationMode.OPERATION_MODE_MAGONLY);
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
            //CurDriveMode = DriveMode.BaseDrive;

            udpServer = new UdpClient(2001);
            remoteEP = new IPEndPoint(IPAddress.Any, 2001);
            client = new UdpClient();
            ep = new IPEndPoint(IPAddress.Parse("192.168.0.25"), 2002);
            client.Connect(ep);
            recieveList = new Queue<byte[]>();
            ParseThread = new Thread(new ThreadStart(parser));
            ParseThread.Start();
            timeout = 20;
            MagFilter = new Average<double>(10);
            servoTurn = 0;
            servoThread = new Thread(new ThreadStart(cameraServoThread));
            servoThread.Start();
        }

        private static void cameraServoThread()
        {
            while (true)
            {
                if (servoTurn == 1)
                {
                    CamereaServo.SetOutput(0.1f);
                    servoTurn = 0;
                }
                else if (servoTurn == 2)
                {
                    CamereaServo.SetOutput(0.0f);
                    servoTurn = 0;
                }
                Thread.Sleep(500);
                CamereaServo.SetOutput(1f);
            }

        }

        public static void parser()
        {
            Console.WriteLine("UDP Parsing thread started");

            //listen for pathing commands only when in autonomous mode
            while (true)
            {
                if (CurDriveMode == DriveMode.toGPS)
                {
                    Byte[] recieveByte = udpServer.Receive(ref remoteEP);
                    recieveList.Enqueue(recieveByte);
                }

            }
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
                        recieveList.Clear();
                        ArrivalServo.SetOutput(0.0f);
                        break;
                    case DriveMode.toGPS:
                        ProcessPathPackets();
                        DrivePackets = new QueueBuffer();
                        ArrivalServo.SetOutput(0.0f);
                        break;
                    case DriveMode.findTennisBall:
                        DrivePackets = new QueueBuffer();
                        ArrivalServo.SetOutput(0.0f);
                        recieveList.Clear();
                        break;
                    case DriveMode.toTennisBall:
                        DrivePackets = new QueueBuffer();
                        ArrivalServo.SetOutput(0.0f);
                        recieveList.Clear();
                        break;
                    case DriveMode.destination:
                        Console.WriteLine("Currently in destination mode");
                        //send notification to Base station UI to display
                        // Moved to pathing location
                        /*Packet Pack = new Packet((byte)PacketID.ArrivalNotification, true);
                        Pack.AppendData(UtilData.ToBytes(1));
                        Client.SendNow(Pack);*/

                        MotorControl.SetRPM(0, 0);
                        MotorControl.SetRPM(2, 0);
                        MotorControl.SetRPM(1, 0);
                        MotorControl.SetRPM(3, 0);

                        DrivePackets = new QueueBuffer();
                        recieveList.Clear();
                        ArrivalServo.SetOutput(0.5f);
                        ArrivalServo.Dispose();
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
                Console.WriteLine("Switching to mode: " + CurDriveMode.ToString());
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
                        if ((sbyte)p.Data.Payload[1] > 0)
                        {
                            servoTurn = 1;
                        }
                        else if ((sbyte)p.Data.Payload[1] < 0)
                        {
                            servoTurn = 2;
                        }
                        //CamereaServo.Dispose();
                        break;
                }
            }
            DrivePackets = new QueueBuffer();
            Console.WriteLine("Finished Base Packets");
        }

        public static void ProcessPathPackets()
        {
            float readHeading = -1f;
            float Lat = -1f;
            float Long = -1f;

            foreach (ISensor Sensor in Sensors)
            {
                if (Sensor is MTK3339)
                {
                    var Tup = ((MTK3339)Sensor).GetCoordinates();
                    Lat = Tup.Item1;
                    Long = Tup.Item2;
                }

                if (Sensor is BNO055)
                {
                    //TODO Maybe get rid of this since we are using filter from readSensorData
                    //readHeading = (float)((BNO055)Sensor).GetTrueHeading();
                    try
                    {
                        var Readings = ((BNO055)Sensor).GetVector(BNO055.VectorType.VECTOR_MAGNETOMETER);

                        double HeadingDirection = 0;

                        if (Readings.Item2 > 0) { HeadingDirection = 90 - (Math.Atan2(Readings.Item1, Readings.Item2) * 180 / Math.PI); }
                        else if (Readings.Item2 < 0) { HeadingDirection = 270 - (Math.Atan(Readings.Item1 / Readings.Item2) * 180 / Math.PI); }
                        else if (Math.Abs(Readings.Item2) <= 1e-6 && Readings.Item1 < 0) { HeadingDirection = 180; }
                        else if (Math.Abs(Readings.Item2) <= 1e-6 && Readings.Item1 > 0) { HeadingDirection = 0; }
                        readHeading = (float)HeadingDirection % 360;

                        readHeading -= 270.0f;
                        if (readHeading < 0f)
                        {
                            readHeading += 360.0f;
                        }
                    }
                    catch
                    {
                        Console.WriteLine("ERROR: Mag wire is loose or disconnected");
                    }

                }
            }

            Console.WriteLine("GPS: " + Lat + "  " + Long + " Mag: " + (float)MagFilter.GetOutput());
            if (readHeading == -1 || Lat == -1)
            {
                // Sensor not read
                // TODO: Give a console error
            }
            else
            {
                readHeading = (float)MagFilter.GetOutput();

                byte[] blat = BitConverter.GetBytes(Lat);
                byte[] blong = BitConverter.GetBytes(Long);
                byte[] bhead = BitConverter.GetBytes(readHeading);

                byte[] sendbytes = new byte[12];

                for (int i = 0; i < 4; i++)
                {
                    sendbytes[i] = blat[i];
                    sendbytes[i + 4] = blong[i];
                    sendbytes[i + 8] = bhead[i];
                }

                try
                {
                    client.Send(sendbytes, sendbytes.Length);
                }
                catch
                {
                    Console.WriteLine("Error: Connection Refuled");
                }

            }

            int speed = 0;
            int turn = 0;
            if (recieveList.Count != 0)
            {
                try
                {
                    Byte[] recieveByte = recieveList.Dequeue();
                    recieveList.Clear(); //may not be needed if communicatoin is slow enough

                    Console.Write("Recieved Data: ");
                    for (int i = 0; i < recieveByte.Length; i++)
                    {
                        Console.Write(recieveByte[i] + " ");
                    }
                    timeout = 20;
                    // Old code for refrence
                    /*
                    string stringData = Encoding.ASCII.GetString(recieveByte);
                    Console.WriteLine("String data: " + stringData);
                    int intData = Convert.ToInt32(stringData);
                    Console.WriteLine("int data: " + intData);
                    Console.WriteLine();
                    float speed = (float)UtilMain.LinearMap(intData, -128, 127, -0.5, 0.5);
                    Console.WriteLine("speed : " + speed);
                    */

                    int desiredHeading = 0;
                    if (recieveByte[0] == 0)
                    {
                        Byte[] speedarray = new Byte[2];
                        speedarray[0] = recieveByte[1];
                        speedarray[1] = recieveByte[2];
                        speed = BitConverter.ToInt16(speedarray, 0);

                        Byte[] headingarray = new Byte[2];
                        headingarray[0] = recieveByte[3];
                        headingarray[1] = recieveByte[4];
                        desiredHeading = BitConverter.ToInt16(headingarray, 0);

                    }
                    else if (recieveByte[0] != 0) //destination reached
                    {
                        CurDriveMode = DriveMode.destination;
                        Console.WriteLine("Recieved byte of 1");

                        Packet Pack = new Packet((byte)PacketID.ArrivalNotification, true);
                        Pack.AppendData(UtilData.ToBytes(1));
                        Client.SendNow(Pack);
                    }

                    if (readHeading != -1)
                    {
                        turn = desiredHeading - Convert.ToInt32(Math.Round(readHeading));
                        if (Math.Abs(turn) > 180)
                        {
                            if (turn < 0) turn += 360;
                            else turn -= 360;
                        }
                        if (turn > 0 && turn <= 30)
                        {
                            turn = 10;
                        }
                        else if ((turn < 0 && turn > -30))
                        {
                            turn = -10;
                        }
                        else
                        {
                            turn = turn / 3;
                        }
                    }
                    GSpeed = speed;
                    Gturn = turn;
                }
                catch
                {
                    Console.WriteLine("ERROR null recieved Byte Array");
                    speed = GSpeed;
                    turn = Gturn;
                    timeout--;
                }

            }
            else if (timeout > 0) //keep sending packets if no data recieved until it times out
            {
                Console.WriteLine("No recieved data. Remining counts: " + timeout);
                speed = GSpeed;
                turn = Gturn;
                timeout--;
            }
            MotorControl.SetRPM(0, (speed - turn));
            MotorControl.SetRPM(2, (speed - turn));
            MotorControl.SetRPM(1, (speed + turn));
            MotorControl.SetRPM(3, (0 - speed - turn));
        }

        public static void SendSensorData(int count)
        {
            Console.WriteLine("Sending snensor data");
            foreach (ISensor Sensor in Sensors)
            {
                if (Sensor is MTK3339)
                {
                    Console.WriteLine("Getting GPS");
                    var Tup = ((MTK3339)Sensor).GetCoordinates();
                    float Lat = Tup.Item1;
                    float Long = Tup.Item2;
                    Packet Pack = new Packet((byte)PacketID.DataGPS, true);
                    Pack.AppendData(UtilData.ToBytes(Lat));
                    Pack.AppendData(UtilData.ToBytes(Long));
                    Client.SendNow(Pack);
                    if (count == 100)
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
                        else if (longDiff < 0)
                        {
                            theta = 270 - theta;
                        }
                        HeadingFromGPSPack.AppendData(UtilData.ToBytes(theta));
                        Client.SendNow(HeadingFromGPSPack);
                        previousCoords = Tup;
                        Console.WriteLine("Sent GPS");
                    }
                }
                if (Sensor is BNO055)
                {
                    //double direction = ((BNO055)Sensor).GetTrueHeading();
                    try
                    {
                        Console.WriteLine("Getting Mag");/*
                        var Readings = ((BNO055)Sensor).GetVector(BNO055.VectorType.VECTOR_MAGNETOMETER);
                        double HeadingDirection = 0;

                        if (Readings.Item2 > 0) { HeadingDirection = 90 - (Math.Atan2(Readings.Item1, Readings.Item2) * 180 / Math.PI); }
                        else if (Readings.Item2 < 0) { HeadingDirection = 270 - (Math.Atan(Readings.Item1 / Readings.Item2) * 180 / Math.PI); }
                        else if (Math.Abs(Readings.Item2) <= 1e-6 && Readings.Item1 < 0) { HeadingDirection = 180; }
                        else if (Math.Abs(Readings.Item2) <= 1e-6 && Readings.Item1 > 0) { HeadingDirection = 0; }
                        double direction = HeadingDirection % 360;
                        direction -= 270.0;
                        if (direction < 0)
                        {
                            direction += 360.0;
                        }
                        MagFilter.Feed(direction);
                        Packet Pack = new Packet((byte)PacketID.DataMagnetometer, true);
                        Pack.AppendData(UtilData.ToBytes(MagFilter.GetOutput()));
                        Client.SendNow(Pack);*/
                        Console.WriteLine("Sent Mag");
                    }
                    catch
                    {
                        Console.WriteLine("ERROR: Mag wire is loose or disconnected");
                    }
                }
            }
            Console.WriteLine("finished snensor data");
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
            Console.WriteLine("Finished the initalize ");
            do
            {
                //Console.WriteLine("Looping");
                //Console.WriteLine("Current mode: " + CurDriveMode);           

                IAsyncResult result;
                Action action = () =>
                {
                    SendSensorData(count);
                };
                result = action.BeginInvoke(null, null);

                if (result.AsyncWaitHandle.WaitOne(10000))
                    Console.WriteLine("Sent All sensor data.");
                else
                    Console.WriteLine("sensor timed out.");


                ProcessInstructions();
                Thread.Sleep(50);
                count++;
                if (count == 101)
                {
                   count = 0;
                } 
            } while (!Quit);
        }
    }
}
