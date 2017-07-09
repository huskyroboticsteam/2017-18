﻿using System;
using System.Linq;
using System.Text;
using System.IO;

namespace Scarlet.Utilities
{
    public static class Log
    {
        // Folder to hold log files
        private const string LogFilesLocation = "/Logs/";

        // Override these in your implementation.
        // OutputType determines which system you see output from.
        // OutputLevel determines the minimum severity required for a message to be output.
        public static Source OutputType = Source.SENSORS;
        public static Severity OutputLevel = Severity.DEBUG;
        public static WriteDestination Destination = WriteDestination.CONSOLE;

        // Override these in your implementation.
        // SystemNames determines what subcomponent an error originated from.
        // ErrorCodes provides a human-readable, concise description for every possible error.
        public static string[] SystemNames;
        public static string[][] ErrorCodes;

        // Location of the Log File. Set in Begin()
        public static StreamWriter LogFile;

        /// <summary>
        /// Outputs a general log message if configured to output this type of message.
        /// </summary>
        /// <param name="Severity">How severe this message is. This partially determines if it is output.</param>
        /// <param name="Src">The system where this log entry is originating.</param>
        /// <param name="Message">The actual log entry to output.</param>
        public static void Output(Severity Sev, Source Src, string Message)
        {
            if(((Sev >= OutputLevel) || (Sev >= Severity.ERROR)) && ((OutputType == Source.ALL) || (Src == OutputType)))
            {
                ForceOutput(Sev, Src, Message);
            }
        }

        /// <summary>
        /// Same as Output, but ignores logging settings and always outputs.
        /// </summary>
        public static void ForceOutput(Severity Sev, Source Src, string Message)
        {
            switch (Sev)
            {
                case Severity.DEBUG:
                    Message = "[DBG] " + Message;
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                case Severity.INFO:
                    Message = "[INF] " + Message;
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case Severity.WARNING:
                    Message = "[WRN] " + Message;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case Severity.ERROR:
                    Message = "[ERR] " + Message;
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case Severity.FATAL:
                    Message = "[FAT] " + Message;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.Red;
                    break;
            }
            Message = "[" + DateTime.Now.ToLongTimeString() + "] " + Message;
            WriteLine(Message);
            Console.ResetColor();
        }

        /// <summary>
        /// Outputs an Exception objet in the form of a stack trace to the console.
        /// Exceptions are always output.
        /// </summary>
        /// <param name="Src">The system that this Exception is originating from.</param>
        /// <param name="Ex">The Exception object.</param>
        public static void Exception(Source Src, Exception Ex)
        {
            string Prefix = "[" + DateTime.Now.ToLongTimeString() + "] [EXC] ";
            string[] Lines = Ex.ToString().Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            Lines = Array.ConvertAll(Lines, Line => (Prefix + Line));
            Lines.ToList().ForEach(Console.WriteLine);
        }

        /// <summary>
        /// Outputs a defined error code to the console.
        /// </summary>
        /// <param name="Error">The error code, in standard form. E.g. 0x0000 for all OK.</param>
        public static void Error(short Error)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            byte System = (byte)(Error >> 8);
            byte Code = (byte)(Error & 0x00FF);
            WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [ERR] [0x" + Error.ToString("X4") + "] [" + SystemNames[System] + "] " + ErrorCodes[System][Code]);
            Console.ResetColor();
        }

        /// <summary>
        /// Outputs a line to the console specifying the logging settings.
        /// </summary>
        public static void Begin()
        {
            StringBuilder Str = new StringBuilder();
            Str.Append('[');
            Str.Append(DateTime.Now.ToLongTimeString());
            Str.Append("] [DBG] Logging started, with minimum level ");
            Str.Append(OutputLevel.ToString());
            Str.Append(" and system ");
            Str.Append(OutputType.ToString());
            Str.Append(". It is ");
            Str.Append(DateTime.Now.ToLongDateString());
            Str.Append(' ');
            Str.Append(DateTime.Now.ToLongTimeString());
            Str.Append('.');

            string FileName = DateTime.Now.ToString("yy-MM-dd-hh-mm-ss-tt");
            FileName = "ScarletLog-" + FileName;
            string[] Files = System.IO.Directory.GetFiles(FileName, "*.log");
            int Iterations = 0;
            while (Files.Contains(FileName + ".log"))
            {
                FileName += "_" + Iterations.ToString();
                Iterations++;
            }
            string FileLocation = LogFilesLocation + FileName + ".log";
            LogFile = new StreamWriter(@FileLocation);
            WriteLine(Str.ToString());
        }

        /// <summary>
        /// Stops the Logging process.
        /// </summary>
        public static void Stop() 
        {
            LogFile.Close();
        }

        private static void Write(string Message)
        {
            if (Log.Destination == WriteDestination.ALL || Log.Destination == WriteDestination.CONSOLE) 
            {
                Console.Write(Message);
            }
            if (Log.Destination == WriteDestination.ALL || Log.Destination == WriteDestination.FILE)
            {
                LogFile.Write(Message);
            }
        }

        private static void WriteLine(string Message)
        {
            Write(Message + "\n");
        }

        /// <summary>
        /// Write destination.
        /// </summary>
        public enum WriteDestination
        {
            CONSOLE, FILE, ALL
        }

        /// <summary>
        /// The subsystem where the error occured, for use in output filtering.
        /// </summary>
        public enum Source
        {
            ALL, MOTORS, NETWORK, GUI, SENSORS, CAMERAS, SUBSYSTEM, OTHER
        }

        /// <summary>
        /// How serious the error is.
        /// </summary>
        /// Debug = Used for program flow debugging and light troubleshooting (e.g. "Starting distance sensor handler")
        /// Information = Used for troubleshooting (e.g. "Distance sensor detected successfully")
        /// Warning = When an issue arises, but functionality sees little to no impact (e.g. "Distance sensor took longer than expected to find value")
        /// Error = When an issue arises that causes significant loss of functionality to one system (e.g. "Distance sensor unreachable")
        /// Fatal = When an issue arises that causes loss of functionality to multiple systems or complete shutdown (e.g. Current limit reached, shutting down all motors)
        public enum Severity
        {
            DEBUG, INFO, WARNING, ERROR, FATAL
        }

    }
}