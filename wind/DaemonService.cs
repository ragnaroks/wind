﻿using PeterKottas.DotNetCore.WindowsService.Interfaces;
using System;
using System.Collections.Generic;

namespace wind {
    public class DaemonService:IMicroService {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality","IDE0052:删除未读的私有成员",Justification = "<挂起>")]
        private List<String> ExtraArguments{get;}=null;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality","IDE0052:删除未读的私有成员",Justification = "<挂起>")]
        private IMicroServiceController MicroServiceController{get;}=null;

        public DaemonService(){}
        public DaemonService(List<String> extraArguments,IMicroServiceController microServiceController){
            this.ExtraArguments=extraArguments;
            this.MicroServiceController=microServiceController;
        }

        public void Start() {
            Helpers.LoggerModuleHelper.TryLog("DaemonService.Start[Warning]","正在启动服务");
            //启动网络监控模块
            _=Program.UnitNetworkCounterModule.StartTraceEventSession();
            //启动远程管理模块,停不下来
            _=Program.RemoteControlModule.Start();
            //启动所有单元
            Program.UnitManageModule.LoadAllUnits();
            Program.UnitManageModule.StartAllAutoUnits(true);
            Helpers.LoggerModuleHelper.TryLog("DaemonService.Start[Warning]","已启动服务");
        }

        public void Stop() {
            Helpers.LoggerModuleHelper.TryLog("DaemonService.Stop[Warning]","正在停止服务");
            //停止网络监控模块
            _=Program.UnitNetworkCounterModule.StopTraceEventSession();
            //停止所有单元
            Program.UnitManageModule.StopAllUnits(false);
            Program.UnitManageModule.RemoveAllUnits(false);
            Helpers.LoggerModuleHelper.TryLog("DaemonService.Stop[Warning]","已停止服务");
        }
    }
}
