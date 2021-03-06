﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using wind.Entities.Common;
using wind.Helpers;

namespace wind.Modules {
    /// <summary>单元管理模块,逻辑上应只提供同步方法,避免此模块出现线程同步问题</summary>
    public class UnitManageModule:IDisposable{
        public Boolean Useable{get;private set;}=false;

        /// <summary>单元存放目录,无路径分隔符</summary>
        private String UnitsDirectory{get;set;}=null;
        /// <summary>单元字典</summary>
        private ConcurrentDictionary<String,Unit> UnitDictionary{get;set;}=new ConcurrentDictionary<String,Unit>();
        
        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if(!disposedValue) {
                if(disposing) {
                    // TODO: 释放托管状态(托管对象)
                    this.StopAllUnits(false);
                }

                // TODO: 释放未托管的资源(未托管的对象)并替代终结器
                // TODO: 将大型字段设置为 null
                disposedValue=true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~UnitManageModule()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="unitsDirectory"></param>
        /// <returns></returns>
        public Boolean Setup(String unitsDirectory){
            if(this.Useable){return true;}
            //检查单元存放路径,不要尝试创建
            if(!Directory.Exists(unitsDirectory)){return false;}
            this.UnitsDirectory=unitsDirectory;
            //完成
            this.Useable=true;
            return true;
        }

        /// <summary>
        /// 单元退出
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnUnitProcessExited(object sender,EventArgs eventArgs) {
            Process exitedProcess=sender as Process;
            Int32 exitedProcessExitCode=exitedProcess.ExitCode;
            Int32 exitedProcessId=exitedProcess.Id;
            exitedProcess.CancelOutputRead();
            exitedProcess.CancelErrorRead();
            exitedProcess.OutputDataReceived-=this.OnProcessOutputDataReceived;
            exitedProcess.ErrorDataReceived-=this.OnProcessErrorDataReceived;
            exitedProcess.Exited-=this.OnUnitProcessExited;
            exitedProcess.Dispose();
            LoggerModuleHelper.TryLog(
                "Modules.UnitManageModule.OnUnitProcessExited",$"进程[#{exitedProcessId}]退出,退出代码[{exitedProcessExitCode}]");
            Unit unit=null;
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary) {
                if(item.Value.ProcessId!=exitedProcessId){continue;}
                unit=item.Value;
                break;
            }
            if(unit==null){return;}
            //if(unit.Process!=null){ unit.Process.Dispose(); }
            if(Program.UnitPerformanceCounterModule.Useable){
                if(!Program.UnitPerformanceCounterModule.Remove(exitedProcessId)){
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.OnUnitProcessExited[Warning]",$"进程[#{exitedProcessId}]单元退出时,没有正确移除性能计数器");
                }
            }
            if(Program.UnitNetworkCounterModule.Useable){
                if(!Program.UnitNetworkCounterModule.Remove(exitedProcessId)){
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.OnUnitProcessExited[Warning]",$"进程[#{exitedProcessId}]单元退出时,没有正确移除网络计数器");
                }
            }
            //如果是单元停止,此时state==3,否则可能是1||2
            if(unit.State==3){return;}
            unit.State=0;
            if(Program.RemoteControlModule.Useable){ Program.RemoteControlModule.StopNotify(unit.Key); }
            Program.LoggerModule.Log("Modules.UnitManageModule.OnUnitProcessExited",$"单元\"{unit.Key}\"异常退出");
            if(!unit.RunningSettings.RestartWhenException){return;}
            this.StartUnit(unit.Key,false);
            Program.LoggerModule.Log("Modules.UnitManageModule.OnUnitProcessExited",$"单元\"{unit.Key}\"已重新启动");
        }
        /// <summary>
        /// 收到单元进程stdout
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="dataReceivedEventArgs"></param>
        private void OnProcessOutputDataReceived(object sender,DataReceivedEventArgs dataReceivedEventArgs) {
            if(!Program.UnitLoggerModule.Useable || this.UnitDictionary.Count<1){return;}
            Process process=sender as Process;
            Int32 processId=process.Id;
            Unit unit=null;
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary) {
                if(item.Value.ProcessId!=processId){continue;}
                unit=item.Value;
                break;
            }
            if(unit==null){return;}
            //记录日志
            Program.UnitLoggerModule.Log(unit.Key,dataReceivedEventArgs.Data);
            //记录输出
            Program.UnitLoggerModule.LogOutput(unit.Key,dataReceivedEventArgs.Data);
            //记录通知
            Program.RemoteControlModule.LogsNotify(unit.Key,dataReceivedEventArgs.Data);
        }
        /// <summary>
        /// 收到单元进程stderr
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="dataReceivedEventArgs"></param>
        private void OnProcessErrorDataReceived(object sender,DataReceivedEventArgs dataReceivedEventArgs) {
            if(!Program.UnitLoggerModule.Useable || this.UnitDictionary.Count<1){return;}
            Process process=sender as Process;
            Int32 processId=process.Id;
            Unit unit=null;
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary) {
                if(item.Value.ProcessId!=processId){continue;}
                unit=item.Value;
                break;
            }
            if(unit==null){return;}
            //记录日志
            Program.UnitLoggerModule.Log(unit.Key,dataReceivedEventArgs.Data);
            //记录输出
            Program.UnitLoggerModule.LogOutput(unit.Key,dataReceivedEventArgs.Data);
            //记录通知
            Program.RemoteControlModule.LogsNotify(unit.Key,dataReceivedEventArgs.Data);
        }

        /// <summary>
        /// 获取单元
        /// </summary>
        /// <param name="unitKey"></param>
        /// <returns></returns>
        public Unit GetUnit(String unitKey){
            if(!this.Useable){return null;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return null;}
            return this.UnitDictionary[unitKey];
        }
        /// <summary>
        /// 获取全部单元
        /// </summary>
        /// <returns></returns>
        public List<Unit> GetUnitList(){
            if(!this.Useable){return null;}
            if(this.UnitDictionary.Count<1){return null;}
            List<Unit> unitList=new List<Unit>();
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary){ unitList.Add(item.Value); }
            return unitList;
        }

        /// <summary>
        /// 停止单元
        /// </summary>
        /// <param name="unitKey"></param>
        public Boolean StopUnit(String unitKey){
            if(!this.Useable){return false;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return false;}
            Unit unit=this.UnitDictionary[unitKey];
            if(unit.State==0 || unit.State==3){return true;}
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.StopUnit",$"正在停止\"{unitKey}\"单元,Type={unit.RunningSettings.Type}");
            unit.State=3;
            if(unit.Process!=null){
                //正常停止单元避免触发退出事件
                try {
                    unit.Process.CancelOutputRead();
                    unit.Process.CancelErrorRead();
                    unit.Process.OutputDataReceived-=this.OnProcessOutputDataReceived;
                    unit.Process.ErrorDataReceived-=this.OnProcessErrorDataReceived;
                    unit.Process.Exited-=this.OnUnitProcessExited;
                } catch {
                    //忽略异常
                }
                //强制杀死单元
                try {
                    switch(unit.RunningSettings.Type) {
                        case 1:
                            unit.Process.Kill(true);
                            ProcessHelper.KillChildProcessByParentProcess(unit.ProcessId);
                            break;
                        default:unit.Process.Kill(true);break;
                    }
                } catch(Exception exception) {
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.StopUnit[Error]",$"停止\"{unitKey}\"单元异常,{exception.Message}\n异常堆栈: {exception.StackTrace}");
                } finally {
                    unit.Process.Dispose();
                    unit.Process=null;
                }
            }
            unit.RunningSettings=null;
            if(Program.UnitPerformanceCounterModule.Useable){
                if(!Program.UnitPerformanceCounterModule.Remove(unit.ProcessId)){
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.StopUnit[Error]",$"停止\"{unitKey}\"单元时没有正确移除性能计数器");
                }
            }
            if(Program.UnitNetworkCounterModule.Useable){
                if(!Program.UnitNetworkCounterModule.Remove(unit.ProcessId)){
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.StopUnit[Error]",$"停止\"{unitKey}\"单元时没有正确移除网络计数器");
                }
            }
            if(Program.RemoteControlModule.Useable){ Program.RemoteControlModule.StopNotify(unit.Key); }
            unit.ProcessId=0;
            unit.State=0;
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.StopUnit",$"已停止\"{unitKey}\"单元");
            return true;
        }

        /// <summary>
        /// 启动单元
        /// </summary>
        /// <param name="unitKey"></param>
        /// <param name="forAutoStart"></param>
        public Int32 StartUnit(String unitKey,Boolean forAutoStart){
            if(!this.Useable){return 0;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return 0;}
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.StartUnit",$"正在启动\"{unitKey}\"单元");
            Unit unit=this.UnitDictionary[unitKey];
            if(unit.State==1 || unit.State==2){return unit.ProcessId;}
            if(unit.State==3){ SpinWait.SpinUntil(()=>false,1000); }
            unit.State=1;
            unit.RunningSettings=unit.Settings.DeepClone();
            ProcessStartInfo processStartInfo=new ProcessStartInfo{
                FileName=unit.RunningSettings.AbsoluteExecutePath,WorkingDirectory=unit.RunningSettings.AbsoluteWorkDirectory,Arguments=unit.RunningSettings.Arguments,
                CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,UseShellExecute=false,
                RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,
                StandardOutputEncoding=UnitManageModuleHelper.GetEncoding(unit.RunningSettings.StandardOutputEncoding),
                StandardErrorEncoding=UnitManageModuleHelper.GetEncoding(unit.RunningSettings.StandardErrorEncoding),
                StandardInputEncoding=UnitManageModuleHelper.GetEncoding(unit.RunningSettings.StandardInputEncoding)
            };
            if(unit.RunningSettings.EnvironmentVariables != null) {
                foreach (KeyValuePair<String,String> item in unit.RunningSettings.EnvironmentVariables) {
                    processStartInfo.EnvironmentVariables[item.Key]=item.Value;
                }
            }
            unit.Process=new Process{StartInfo=processStartInfo,EnableRaisingEvents=true};
            unit.Process.Exited+=this.OnUnitProcessExited;
            unit.Process.OutputDataReceived+=this.OnProcessOutputDataReceived;
            unit.Process.ErrorDataReceived+=this.OnProcessErrorDataReceived;
            if(forAutoStart && unit.RunningSettings.AutoStartDelay>0){ SpinWait.SpinUntil(()=>false,unit.RunningSettings.AutoStartDelay*1000); }
            try {
                if(!unit.Process.Start()) {
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.StartUnit",$"启动\"{unitKey}\"单元失败");
                    unit.State=0;
                    if(Program.RemoteControlModule.Useable){ Program.RemoteControlModule.StopNotify(unitKey); }
                    return 0;
                }
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog(
                    "Modules.UnitManageModule.StartUnit[Error]",
                    $"启动\"{unitKey}\"单元异常,{exception.Message}\n异常堆栈: {exception.StackTrace}");
                unit.State=0;
                if(Program.RemoteControlModule.Useable){ Program.RemoteControlModule.StopNotify(unitKey); }
                return 0;
            }
            if(!String.IsNullOrWhiteSpace(unit.RunningSettings.PriorityClass)) {
                try{
                    unit.Process.PriorityClass=UnitManageModuleHelper.GetProcessPriorityClassFromString(unit.RunningSettings.PriorityClass);
                }catch (Exception exception){
                    LoggerModuleHelper.TryLog(
                        "Modules.UnitManageModule.StartUnit[Error]",$"设置\"{unitKey}\"单元优先级异常,{exception.Message}\n异常堆栈: {exception.StackTrace}");
                }
            }
            if(!String.IsNullOrWhiteSpace(unit.RunningSettings.ProcessorAffinity)){
                Nullable<IntPtr> ptr=UnitManageModuleHelper.GetProcessorAffinityFormString(unit.RunningSettings.ProcessorAffinity);
                if(ptr.HasValue){
                    try{
                        unit.Process.ProcessorAffinity=ptr.Value;
                    }catch (Exception exception){
                        LoggerModuleHelper.TryLog(
                            "Modules.UnitManageModule.StartUnit[Error]",$"设置\"{unitKey}\"单元亲和性异常,{exception.Message}\n异常堆栈: {exception.StackTrace}");
                    }
                }
            }
            unit.Process.BeginOutputReadLine();
            unit.Process.BeginErrorReadLine();
            //unit.Process.StandardInput.AutoFlush=true;
            unit.ProcessId=unit.Process.Id;
            unit.State=2;
            if(unit.RunningSettings.MonitorPerformanceUsage && Program.UnitPerformanceCounterModule.Useable){
                Program.UnitPerformanceCounterModule.Add(unit.ProcessId);
            }
            if(unit.RunningSettings.MonitorNetworkUsage && Program.UnitNetworkCounterModule.Useable) {
                _=Program.UnitNetworkCounterModule.Add(unit.ProcessId);
            }
            if(Program.RemoteControlModule.Useable){ Program.RemoteControlModule.StartNotify(unit.Key); }
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.StartUnit",$"已启动\"{unitKey}\"单元");
            return unit.ProcessId;
        }

        /// <summary>
        /// 重启单元
        /// </summary>
        /// <param name="unitKey"></param>
        /// <returns></returns>
        public Boolean RestartUnit(String unitKey) {
            if(!this.Useable){return false;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return false;}
            switch(this.UnitDictionary[unitKey].State) {
                case 0:return this.StartUnit(unitKey,false)>0;
                case 2:return this.StopUnit(unitKey) && this.StartUnit(unitKey,false)>0;
                default:return false;
            }
        }

        /// <summary>
        /// 移除单元
        /// </summary>
        /// <param name="unitKey"></param>
        public Boolean RemoveUnit(String unitKey) {
            if(!this.Useable){return false;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return false;}
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.RemoveUnit",$"正在移除\"{unitKey}\"单元");
            if(!this.StopUnit(unitKey)) {
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.RemoveUnit",$"停止\"{unitKey}\"单元失败");
                return false;
            }
            Boolean b1=this.UnitDictionary.TryRemove(unitKey,out _);
            if(b1) {
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.RemoveUnit",$"已移除\"{unitKey}\"单元");
            } else {
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.RemoveUnit",$"移除\"{unitKey}\"单元失败");
            }
            return b1;
        }

        /// <summary>
        /// 加载单元
        /// </summary>
        /// <param name="unitKey"></param>
        /// <returns></returns>
        public UnitSettings LoadUnit(String unitKey){
            if(!this.Useable){return null;}
            /*特殊处理*/if(unitKey=="self" || unitKey=="wind" || unitKey=="daemon"){return null;}/*特殊处理*/
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadUnit","开始加载单元配置文件");
            //读取文件
            String unitFilePath=String.Concat(this.UnitsDirectory,Path.DirectorySeparatorChar,unitKey,".json");
            if(!File.Exists(unitFilePath)){
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadUnit[Error]",$"单元配置文件 {unitFilePath} 不存在");
                return null;
            }
            FileInfo fileInfo;
            try {
                fileInfo=new FileInfo(unitFilePath);
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog(
                    "Modules.UnitManageModule.LoadUnit[Error]",
                    $"单元配置文件 {unitFilePath} 文件信息异常,: {exception.Message}\n异常堆栈: {exception.StackTrace}");
                return null;
            }
            //setting
            UnitSettings unitSettings=UnitManageModuleHelper.ParseUnitSettingsFile(fileInfo);
            if(unitSettings==null){
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadUnit[Warning]",$"单元文件\"{unitFilePath}\"读取失败");
                return null;
            }
            //检查是新增或更新
            if(this.UnitDictionary.ContainsKey(unitKey)) {
                this.UnitDictionary[unitKey].Settings=unitSettings;
            } else {
                _=this.UnitDictionary.TryAdd(unitKey,new Unit{Key=unitKey,Settings=unitSettings});
            }
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadUnit",$"单元\"{unitKey}\"读取成功,已加入单元列表");
            return unitSettings;
        }

        /// <summary>
        /// 解析所有单元配置文件
        /// </summary>
        /// <returns>解析成功单元列表</returns>
        public List<UnitSettings> LoadAllUnits(){
            if(!this.Useable){return null;}
            //读取文件目录
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadAllUnits","开始解析所有单元配置文件");
            FileInfo[] fileInfoArray;
            try {
                DirectoryInfo directoryInfo=new DirectoryInfo(this.UnitsDirectory);
                fileInfoArray=directoryInfo.GetFiles("*.json",SearchOption.TopDirectoryOnly);
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog(
                    "Modules.UnitManageModule.LoadAllUnits[Error]",
                    $"读取单元存放目录异常,{exception.Message}\n异常堆栈: {exception.StackTrace}");
                return null;
            }
            if(fileInfoArray.Length<1){return null;}
            //解析文件
            List<UnitSettings> unitSettingsList=new List<UnitSettings>();
            for(Int32 i1=0;i1<fileInfoArray.Length;i1++){
                //key
                String unitKey=UnitManageModuleHelper.GetUnitKey(fileInfoArray[i1]);
                /*特殊处理*/if(unitKey=="self" || unitKey=="wind" || unitKey=="daemon"){continue;}/*特殊处理*/
                if(String.IsNullOrWhiteSpace(unitKey) || unitKey.Length>32){
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadAllUnits[Warning]",$"单元文件\"{fileInfoArray[i1].FullName}\"标识错误,已跳过");
                    continue;
                }
                //setting
                UnitSettings unitSettings=UnitManageModuleHelper.ParseUnitSettingsFile(fileInfoArray[i1]);
                if(unitSettings==null) {
                    LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadAllUnits[Warning]",$"单元文件\"{fileInfoArray[i1].FullName}\"读取失败,已跳过");
                    continue;
                }
                //检查是新增或更新
                if(this.UnitDictionary.ContainsKey(unitKey)) {
                    this.UnitDictionary[unitKey].Settings=unitSettings;
                } else {
                    _=this.UnitDictionary.TryAdd(unitKey,new Unit{Key=unitKey,Settings=unitSettings});
                }
                unitSettingsList.Add(unitSettings);
                LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadAllUnits",$"单元\"{unitKey}\"读取成功,已加入单元列表");
            }
            //完成
            LoggerModuleHelper.TryLog("Modules.UnitManageModule.LoadAllUnits",$"已解析 {this.UnitDictionary.Count} 个单元配置文件");
            return this.UnitDictionary.Count>0?unitSettingsList:null;
        }

        /// <summary>
        /// 启动全部单元
        /// </summary>
        /// <param name="asyncTask">异步处理</param>
        public void StartAllAutoUnits(Boolean asyncTask){
            if(!this.Useable){return;}
            if(this.UnitDictionary.Count<1){return;}
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary) {
                if(!item.Value.Settings.AutoStart){continue;}
                if(asyncTask) {
                    Task.Run(()=>{ this.StartUnit(item.Key,true); });
                } else {
                    this.StartUnit(item.Key,true);
                }
            }
        }

        /// <summary>
        /// 启动全部单元
        /// </summary>
        /// <param name="asyncTask">异步处理</param>
        public void StartAllUnits(Boolean asyncTask){
            if(!this.Useable){return;}
            if(this.UnitDictionary.Count<1){return;}
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary){
                if(asyncTask) {
                    Task.Run(()=>{ this.StartUnit(item.Key,false); });
                }else{
                    this.StartUnit(item.Key,false);
                }
            }
        }

        /// <summary>
        /// 停止全部单元
        /// </summary>
        /// <param name="asyncTask">异步处理</param>
        public void StopAllUnits(Boolean asyncTask){
            if(!this.Useable){return;}
            if(this.UnitDictionary.Count<1){return;}
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary){
                if(asyncTask) {
                    Task.Run(()=>{ this.StopUnit(item.Key); });
                } else {
                    this.StopUnit(item.Key);
                }
            }
        }

        /// <summary>
        /// 重启全部(正在运行的)单元
        /// </summary>
        /// <param name="asyncTask">异步处理</param>
        public void RestartAllUnits(Boolean asyncTask){
            if(!this.Useable){return;}
            if(this.UnitDictionary.Count<1){return;}
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary) {
                if(item.Value.State!=2){continue;}
                if(asyncTask) {
                    Task.Run(()=>{ this.RestartUnit(item.Key); });
                } else {
                    this.RestartUnit(item.Key);
                }
            }
        }

        /// <summary>
        /// 移除全部单元
        /// </summary>
        /// <param name="asyncTask">异步处理</param>
        public void RemoveAllUnits(Boolean asyncTask) {
            if(!this.Useable){return;}
            if(this.UnitDictionary.Count<1){return;}
            foreach(KeyValuePair<String,Unit> item in this.UnitDictionary){
                if(asyncTask) {
                    Task.Run(()=>{ this.RemoveUnit(item.Key); });
                } else {
                    this.RemoveUnit(item.Key);
                }
            }
        }

        public Boolean ExecuteCommand(String unitKey,String commandLine){
            if(!this.Useable){return false;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return false;}
            if(this.UnitDictionary[unitKey].State!=2){return false;}
            if(String.IsNullOrWhiteSpace(commandLine)){return false;}
            try {
                this.UnitDictionary[unitKey].Process.Refresh();
                this.UnitDictionary[unitKey].Process.StandardInput.WriteLine(commandLine);
                /*this.UnitDictionary[unitKey].Process.StandardInput.Write(commandLine+this.UnitDictionary[unitKey].Process.StandardInput.NewLine);
                this.UnitDictionary[unitKey].Process.StandardInput.Flush();*/
                //记录日志
                Program.UnitLoggerModule.Log(unitKey,"> "+commandLine);
                //记录输出
                Program.UnitLoggerModule.LogOutput(unitKey,"> "+commandLine);
                //记录通知
                Program.RemoteControlModule.LogsNotify(unitKey,"> "+commandLine);
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog(
                    "Modules.UnitManageModule.ExecuteCommand[Error]",$"单元输入指令异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return false;
            }
            return true;
        }

        public Boolean ExecuteExitCommand(String unitKey){
            if(!this.Useable){return false;}
            if(this.UnitDictionary.Count<1 || !this.UnitDictionary.ContainsKey(unitKey)){return false;}
            if(this.UnitDictionary[unitKey].State!=2){return false;}
            try {
                this.UnitDictionary[unitKey].Process.Refresh();
                this.UnitDictionary[unitKey].Process.StandardInput.Write("\x3"+this.UnitDictionary[unitKey].Process.StandardInput.NewLine);
                this.UnitDictionary[unitKey].Process.StandardInput.Flush();
                //this.UnitDictionary[unitKey].Process.StandardInput.Close();
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog(
                    "Modules.UnitManageModule.ExecuteExitCommand[Error]",$"单元输入指令异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return false;
            }
            return true;
        }
    }
}
