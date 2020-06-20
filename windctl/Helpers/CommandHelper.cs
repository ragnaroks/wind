﻿using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using wind.Entities.Protobuf;

namespace windctl.Helpers {
    public static class CommandHelper {
        private const String HELP
            ="windctl [Any]                =>  print this help\n"
            +"windctl version              =>  print windctl's version\n"
            +"windctl status <unitKey>     =>  get unit's status\n"
            //+"windctl start <unitKey>      =>  start unit\n"
            //+"windctl stop <unitKey>       =>  stop unit\n"
            //+"windctl restart <unitKey>    =>  restart unit\n"
            //+"windctl load <unitKey>       =>  try load/update unit's settings from file,need restart to apply\n"
            //+"windctl remove <unitKey>     =>  stop unit and remove it,it can not be start again\n"
            //+"windctl status-all           =>  get all unit's lite status\n"
            //+"windctl start-all            =>  start all unit\n"
            //+"windctl stop-all             =>  stop all unit\n"
            //+"windctl restart-all          =>  restart all unit\n"
            //+"windctl load-all             =>  try load/update all units's settings from file,need restart to apply\n"
            //+"windctl remove-all           =>  stop all unit and remove them,they can not be start again\n"
            //+"windctl daemon-version       =>  get daemon service's version\n"
            //+"windctl daemon-status        =>  get daemon service's status\n"
            //+"windctl daemon-shutdown      =>  shutdown daemon service\n";
            ;
        private const String ERROR_RESPONSE="command execute failed,response is empty or incorrect";

        /// <summary>
        /// 是否远程控制指令
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public static Boolean IsRemoteCommand(String command){
            switch(command){
                case "status":
                case "start":
                case "stop":
                case "restart":
                case "load":
                case "remove":
                case "attach":
                case "status-all":
                case "start-all":
                case "stop-all":
                case "restart-all":
                case "load-all":
                case "remove-all":
                case "daemon-version":
                case "daemon-status":
                case "daemon-shutdown":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 是否远程控制指令
        /// </summary>
        /// <param name="command"></param>
        /// <param name="unitKey"></param>
        /// <returns></returns>
        public static Boolean ValidUnitKey(String command,String unitKey){
            switch(command){
                case "status":
                case "start":
                case "stop":
                case "restart":
                case "load":
                case "remove":
                case "attach":
                    break;
                default:return true;
            }
            if(String.IsNullOrWhiteSpace(unitKey) || unitKey.Length>32){return false;}
            Regex regex=new Regex(@"^\S+$",RegexOptions.Compiled);
            if(!regex.IsMatch(unitKey)){return false;}
            return true;
        }

        /// <summary>
        /// windctl help
        /// </summary>
        public static void Help()=>Console.Write(HELP);
        /// <summary>
        /// windctl version
        /// </summary>
        public static void Version() {
            Version version=Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"windctl v{version}");
        }

        /// <summary>
        /// windctl status unitKey
        /// </summary>
        public static void Status(StatusResponseProtobuf statusResponseProtobuf){
            if(statusResponseProtobuf==null){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            if(!statusResponseProtobuf.Executed) {
                Console.WriteLine(String.Concat("command execute failed,",statusResponseProtobuf.NoExecuteMessage));
                return;
            }
            //第一行
            if(statusResponseProtobuf.UnitProtobuf.State==2){ Console.ForegroundColor=ConsoleColor.Green; }
            Console.Write("● ");
            if(statusResponseProtobuf.UnitProtobuf.State==2){ Console.ResetColor(); }
            UnitSettingsProtobuf unitSettingsProtobuf=statusResponseProtobuf.UnitProtobuf.State==2
                ?statusResponseProtobuf.UnitProtobuf.RunningSettingsProtobuf
                :statusResponseProtobuf.UnitProtobuf.SettingsProtobuf;
            Console.Write($"{unitSettingsProtobuf.Name} - {unitSettingsProtobuf.Description}");
            //第二行
            Console.Write($"\n     Loaded:  {statusResponseProtobuf.UnitProtobuf.SettingsFilePath}");
            if(unitSettingsProtobuf.AutoStart){ Console.Write("; auto;"); }
            //第三行
            Console.Write($"\n      State:  ");
            switch(statusResponseProtobuf.UnitProtobuf.State) {
                case 0:Console.Write("stopped");break;
                case 1:Console.Write("starting");break;
                case 2:
                    Console.ForegroundColor=ConsoleColor.Green;
                    Console.Write("running");
                    Console.ResetColor();
                    Console.Write(' ');
                    Console.ForegroundColor=ConsoleColor.Cyan;
                    Console.Write($"#{statusResponseProtobuf.UnitProtobuf.ProcessProtobuf.Id}");
                    Console.ResetColor();
                    String startTimeString=statusResponseProtobuf.UnitProtobuf.ProcessProtobuf.StartTime.ToLocalTimestampString();
                    Console.Write($" (since {startTimeString})");
                    break;
                case 3:Console.Write("stopping");break;
                default:Console.Write("unknown");break;
            }
            //第四行
            if(statusResponseProtobuf.UnitProtobuf.State==2 && unitSettingsProtobuf.MonitorPerformanceUsage) {
                String cpuValue=String.Format(CultureInfo.InvariantCulture,"{0:N1} %",statusResponseProtobuf.UnitProtobuf.PerformanceCounterProtobuf.CPU);
                String ramValue=statusResponseProtobuf.UnitProtobuf.PerformanceCounterProtobuf.RAM.FixedByteSize();
                Console.Write($"\nPerformance:  {cpuValue}; {ramValue}");
            }
            //第五行
            if(statusResponseProtobuf.UnitProtobuf.State==2 && unitSettingsProtobuf.MonitorNetworkUsage) {
                String sendSpeed=statusResponseProtobuf.UnitProtobuf.NetworkCounterProtobuf.SendSpeed.FixedByteSize();
                String receiveSpeed=statusResponseProtobuf.UnitProtobuf.NetworkCounterProtobuf.ReceiveSpeed.FixedByteSize();
                String totalSent=statusResponseProtobuf.UnitProtobuf.NetworkCounterProtobuf.TotalSent.FixedByteSize();
                String totalReceived=statusResponseProtobuf.UnitProtobuf.NetworkCounterProtobuf.TotalReceived.FixedByteSize();
                Console.Write($"\n    Network:  ↑ {sendSpeed}/s,{totalSent}; ↓ {receiveSpeed}/s,{totalReceived}");
            }
            Console.WriteLine();
        }

        /*

        /// <summary>
        /// windctl start unitKey
        /// </summary>
        public static void Start(String unitKey){
            if(String.IsNullOrWhiteSpace(unitKey)) {
                Console.WriteLine(ERROR_INVALID);
                return;
            }
            StartRequestProtobuf startRequestProtobuf=new StartRequestProtobuf{Type=1002,UnitKey=unitKey};
            Byte[] request=startRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.Start[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            StartResponseProtobuf startResponseProtobuf=StartResponseProtobuf.Parser.ParseFrom(response);
            if(startResponseProtobuf==null || startResponseProtobuf.Type!=2002){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            if(!startResponseProtobuf.Executed) {
                Console.WriteLine(String.Concat("command execute failed,",startResponseProtobuf.NoExecuteMessage));
                return;
            }
            Console.WriteLine($"command executed,unit {unitKey} starting");
        }
        /// <summary>
        /// windctl stop unitKey
        /// </summary>
        public static void Stop(String unitKey){
            if(String.IsNullOrWhiteSpace(unitKey)) {
                Console.WriteLine(ERROR_INVALID);
                return;
            }
            StopRequestProtobuf stopRequestProtobuf=new StopRequestProtobuf{Type=1003,UnitKey=unitKey};
            Byte[] request=stopRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.Stop[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            StopResponseProtobuf stopResponseProtobuf=StopResponseProtobuf.Parser.ParseFrom(response);
            if(stopResponseProtobuf==null || stopResponseProtobuf.Type!=2003){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            if(!stopResponseProtobuf.Executed) {
                Console.WriteLine(String.Concat("command execute failed,",stopResponseProtobuf.NoExecuteMessage));
                return;
            }
            Console.WriteLine($"command executed,unit {unitKey} stopping");
        }
        /// <summary>
        /// windctl restart unitKey
        /// </summary>
        public static void Restart(String unitKey){
            if(String.IsNullOrWhiteSpace(unitKey)) {
                Console.WriteLine(ERROR_INVALID);
                return;
            }
            RestartRequestProtobuf restartRequestProtobuf=new RestartRequestProtobuf{Type=1004,UnitKey=unitKey};
            Byte[] request=restartRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.Restart[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            RestartResponseProtobuf restartResponseProtobuf=RestartResponseProtobuf.Parser.ParseFrom(response);
            if(restartResponseProtobuf==null || restartResponseProtobuf.Type!=2004){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            if(!restartResponseProtobuf.Executed) {
                Console.WriteLine(String.Concat("command execute failed,",restartResponseProtobuf.NoExecuteMessage));
                return;
            }
            Console.WriteLine($"command executed,unit {unitKey} restarting");
        }

        /// <summary>
        /// windctl daemon-version
        /// </summary>
        public static void DaemonVersion() {
            DaemonVersionRequestProtobuf daemonVersionRequestProtobuf=new DaemonVersionRequestProtobuf{Type=1200};
            Byte[] request=daemonVersionRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.DaemonVersion[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            DaemonVersionResponseProtobuf daemonVersionResponseProtobuf=DaemonVersionResponseProtobuf.Parser.ParseFrom(response);
            if(daemonVersionResponseProtobuf==null || daemonVersionResponseProtobuf.Type!=2200){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            Version version=Assembly.GetExecutingAssembly().GetName().Version;
            if(version.Major==daemonVersionResponseProtobuf.MajorVersion
            && version.Minor==daemonVersionResponseProtobuf.MinorVersion
            && version.Build==daemonVersionResponseProtobuf.BuildVersion
            && version.Revision==daemonVersionResponseProtobuf.RevisionVersion){
                Console.WriteLine($"wind v{daemonVersionResponseProtobuf.MajorVersion}.{daemonVersionResponseProtobuf.MinorVersion}.{daemonVersionResponseProtobuf.BuildVersion}.{daemonVersionResponseProtobuf.RevisionVersion},same version with windctl");
            } else {
                Console.WriteLine($"wind v{daemonVersionResponseProtobuf.MajorVersion}.{daemonVersionResponseProtobuf.MinorVersion}.{daemonVersionResponseProtobuf.BuildVersion}.{daemonVersionResponseProtobuf.RevisionVersion}");
            }
        }
        /// <summary>
        /// windctl daemon-status
        /// </summary>
        public static void DaemonStatus() {
            DaemonStatusRequestProtobuf daemonStatusRequestProtobuf=new DaemonStatusRequestProtobuf{Type=1201};
            Byte[] request=daemonStatusRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.DaemonStatus[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            DaemonStatusResponseProtobuf daemonStatusResponseProtobuf=DaemonStatusResponseProtobuf.Parser.ParseFrom(response);
            if(daemonStatusResponseProtobuf==null || daemonStatusResponseProtobuf.Type!=2201){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            //第一行
            Console.ForegroundColor=ConsoleColor.Green;
            Console.Write("● ");
            Console.ResetColor();
            Console.Write($"{daemonStatusResponseProtobuf.DaemonProtobuf.Name} - {daemonStatusResponseProtobuf.DaemonProtobuf.Description}");
            //第二行
            Console.Write($"\n     Loaded:  {daemonStatusResponseProtobuf.DaemonProtobuf.AbsoluteWorkDirectory}Data\\AppSettings.json");
            //第三行
            Console.Write($"\n      State:  ");
            Console.ForegroundColor=ConsoleColor.Green;
            Console.Write("running");
            Console.ResetColor();
            Console.Write(' ');
            Console.ForegroundColor=ConsoleColor.Cyan;
            Console.Write($" #{daemonStatusResponseProtobuf.DaemonProtobuf.ProcessProtobuf.Id}");
            Console.ResetColor();
            String startTimeString=daemonStatusResponseProtobuf.DaemonProtobuf.ProcessProtobuf.StartTime.ToLocalTimestampString();
            Console.Write($" (since {startTimeString})");
            //第四行
            String cpuValue=String.Format(CultureInfo.InvariantCulture,"{0:N1} %",daemonStatusResponseProtobuf.DaemonProtobuf.PerformanceCounterProtobuf.CPU);
            String ramValue=daemonStatusResponseProtobuf.DaemonProtobuf.PerformanceCounterProtobuf.RAM.FixedByteSize();
            Console.Write($"\nPerformance:  {cpuValue}; {ramValue}");
            //第五行
            String sendSpeed=daemonStatusResponseProtobuf.DaemonProtobuf.NetworkCounterProtobuf.SendSpeed.FixedByteSize();
            String receiveSpeed=daemonStatusResponseProtobuf.DaemonProtobuf.NetworkCounterProtobuf.ReceiveSpeed.FixedByteSize();
            String totalSent=daemonStatusResponseProtobuf.DaemonProtobuf.NetworkCounterProtobuf.TotalSent.FixedByteSize();
            String totalReceived=daemonStatusResponseProtobuf.DaemonProtobuf.NetworkCounterProtobuf.TotalReceived.FixedByteSize();
            Console.Write($"\n    Network:  ↑ {sendSpeed}/s,{totalSent}; ↓ {receiveSpeed}/s,{totalReceived}");
            Console.WriteLine();
        }
        /// <summary>
        /// windctl daemon-shutdoown
        /// </summary>
        public static void DaemonShutdown() {
            DaemonShutdownRequestProtobuf daemonShutdownRequestProtobuf=new DaemonShutdownRequestProtobuf{Type=1299};
            Byte[] request=daemonShutdownRequestProtobuf.ToByteArray();
            if(request.GetLength(0)>100){return;}
            Byte[] buffer;
            Int32 bufferSize;
            try {
                NamedPipeClientStream namedPipeClientStream=new NamedPipeClientStream(".",Program.AppEnvironment.PipelineName,PipeDirection.InOut,PipeOptions.WriteThrough);
                namedPipeClientStream.Connect(1000);
                //发送
                namedPipeClientStream.Write(request);
                namedPipeClientStream.Flush();
                //回复
                buffer=new Byte[1048576];
                bufferSize=namedPipeClientStream.Read(buffer);
                //释放
                namedPipeClientStream.Dispose();
            }catch(Exception exception){
                LoggerModuleHelper.TryLog("Helpers.CommandHelper.DaemonShutdown[Error]",$"管道通信异常\n异常信息:{exception.Message}\n异常堆栈:{exception.StackTrace}");
                Console.WriteLine($"command execute error,{exception.Message}");
                return;
            }
            if(bufferSize<1){
                Console.WriteLine(NO_RESPONSE);
                return;
            }
            Byte[] response=buffer.AsSpan(0,bufferSize).ToArray();
            DaemonShutdownResponseProtobuf daemonShutdownResponseProtobuf=DaemonShutdownResponseProtobuf.Parser.ParseFrom(response);
            if(daemonShutdownResponseProtobuf==null || daemonShutdownResponseProtobuf.Type!=2299){
                Console.WriteLine(ERROR_RESPONSE);
                return;
            }
            Console.WriteLine("wind is shutting");
        }
        */
    }
}