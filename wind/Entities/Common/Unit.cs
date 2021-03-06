﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace wind.Entities.Common {
    public class Unit {
        /// <summary>单元名称(内部标识用)</summary>
        public String Key{get;set;}=null;
        /// <summary>单元运行状态,0:已停止,1:正在启动,2:正在运行,3:正在停止</summary>
        public Int32 State{get;set;}=0;
        /// <summary>单元配置</summary>
        public UnitSettings Settings{get;set;}=null;
        /// <summary>使用的单元配置</summary>
        public UnitSettings RunningSettings{get;set;}=null;
        /// <summary>单元进程</summary>
        public Process Process{get;set;}=null;
        /// <summary>单元进程id,用于退出检测</summary>
        public Int32 ProcessId{get;set;}=0;
    }
}
