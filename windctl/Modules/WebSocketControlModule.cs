﻿using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WatsonWebsocket;
using wind.Entities.Protobuf;
using windctl.Helpers;

namespace windctl.Modules {
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance","CA1822:将成员标记为 static",Justification = "<挂起>")]
    public class WebSocketControlModule:IDisposable{
        public Boolean Useable{get;private set;}=false;

        /// <summary>ipv4正则</summary>
        private Regex RegexAddress4{get;}=new Regex(@"^[0-9\.]{7,15}$",RegexOptions.Compiled);
        /// <summary>ipv6正则</summary>
        private Regex RegexAddress6{get;}=new Regex(@"^[a-f0-9\:\[\]]{5,41}$",RegexOptions.Compiled);
        /// <summary>远程控制Key正则</summary>
        private Regex RegexControlKey{get;}=new Regex(@"^\S{32,4096}$",RegexOptions.Compiled);
        /// <summary>远程控制Key</summary>
        private String ControlKey{get;set;}=null;
        /// <summary>websocket客户端</summary>
        private WatsonWsClient Client{get;set;}=null;
        /// <summary>定时器</summary>
        private Timer Timer{get;set;}=null;
        /// <summary>是否启用定时器</summary>
        private Boolean TimerEnable{get;set;}=false;
        /// <summary>客户端链接GUID</summary>
        private String ClientConnectionId{get;set;}=null;
        /// <summary>客户端链接是否有效</summary>
        private Boolean ClientConnectionValid{get;set;}=false;
        
        #region IDisposable
        private bool disposedValue;
        protected virtual void Dispose(bool disposing) {
            if(!disposedValue) {
                if(disposing) {
                    // TODO: 释放托管状态(托管对象)
                    if(this.Client!=null) {
                        //this.Client.ServerConnected-=this.ServerConnected;
                        this.Client.ServerDisconnected-=this.ServerDisconnected;
                        this.Client.MessageReceived-=this.ClientMessageReceived;
                        this.Client.Dispose();
                    }
                    this.Timer?.Dispose();
                }

                // TODO: 释放未托管的资源(未托管的对象)并替代终结器
                // TODO: 将大型字段设置为 null
                disposedValue=true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~WebSocketControlModule()
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
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="controlKey"></param>
        /// <returns></returns>
        public Boolean Setup(String address,Int32 port,String controlKey){
            if(this.Useable){return true;}
            //校验参数
            if(String.IsNullOrWhiteSpace(address) || port>Int16.MaxValue || port<1024){
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"初始化模块失败,参数错误\naddress:{address},port:{port}");
                return false;
            }
            if(address=="localhost"){ address="127.0.0.1"; }
            Boolean isV4=this.RegexAddress4.IsMatch(address);
            /*Boolean isV6=this.RegexAddress6.IsMatch(address);*/
            if(!isV4/* && !isV6*/){
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"初始化模块失败,参数错误\naddress:{address},port:{port}");
                return false;
            }
            if(String.IsNullOrWhiteSpace(controlKey) || !this.RegexControlKey.IsMatch(controlKey)) {
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"初始化模块失败,参数错误\ncontrolKey:{controlKey}");
                return false;
            }
            this.ControlKey=controlKey;
            //初始化定时器
            try {
                this.Timer=new Timer(this.TimerCallback,null,0,8000);
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"初始化定时器异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return false;
            }
            this.TimerEnable=true;
            //初始化客户端
            try {
                this.Client=new WatsonWsClient(address,port,false);
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"初始化客户端异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return false;
            }
            //this.Client.ServerConnected+=this.ServerConnected;
            this.Client.ServerDisconnected+=this.ServerDisconnected;
            this.Client.MessageReceived+=this.ClientMessageReceived;
            //完成
            this.Useable=true;
            return true;
        }

        /// <summary>
        /// 链接服务端
        /// </summary>
        /// <returns></returns>
        public Boolean Start() {
            try {
                this.Client.Start();
            }catch(Exception exception) {
                LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.Setup[Error]",$"链接服务器异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return false;
            }
            SpinWait.SpinUntil(()=>this.Client.Connected,4000);
            return this.Client.Connected;//true or false(timeout)
        }

        /// <summary>
        /// 等待验证
        /// </summary>
        /// <returns></returns>
        public Boolean Valid() {
            SpinWait.SpinUntil(()=>this.ClientConnectionValid,4000);
            return this.ClientConnectionValid;
        }

        /// <summary>
        /// 定时器任务
        /// </summary>
        /// <param name="state"></param>
        private void TimerCallback(Object state) {
            if(!this.Useable || !this.TimerEnable){return;}
            this.TimerEnable=false;
            this.ClientKeepAlive();
            this.TimerEnable=true;
        }

        /// <summary>
        /// 断开链接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ServerDisconnected(object sender,EventArgs e) {
            if(!String.IsNullOrWhiteSpace(Program.AttachedUnitKey)){
                Program.AttachedUnitKey=null;
                Program.InAction=false;
                Console.ForegroundColor=ConsoleColor.Red;
                Console.WriteLine("dettached from unit,because connection was broken");
                Console.ResetColor();
                Environment.Exit(0);
                return;
            }
            if(Program.InAction){ Program.InAction=false; }
        }
        /// <summary>
        /// 收到服务端数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="messageReceivedEventArgs"></param>
        private void ClientMessageReceived(object sender,MessageReceivedEventArgs messageReceivedEventArgs){
            //解析数据
            if(messageReceivedEventArgs.Data.GetLength(0)<1){return;}
            PacketTestProtobuf packetTestProtobuf;
            try {
                packetTestProtobuf=PacketTestProtobuf.Parser.ParseFrom(messageReceivedEventArgs.Data);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.ClientMessageReceived[Error]",
                    $"探测数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //分拣处理
            LoggerModuleHelper.TryLog(
                "Modules.WebSocketControlModule.ClientMessageReceived",
                $"分拣处理 {packetTestProtobuf.Type} in {messageReceivedEventArgs.Data.GetLength(0)} bytes");
            switch(packetTestProtobuf.Type) {
                case 21:this.ServerAcceptConnection(messageReceivedEventArgs.Data);break;
                case 22:this.ServerValidateConnection(messageReceivedEventArgs.Data);break;
                //
                case 2001:this.StatusResponse(messageReceivedEventArgs.Data);break;
                case 2002:this.StartResponse(messageReceivedEventArgs.Data);break;
                case 2003:this.StopResponse(messageReceivedEventArgs.Data);break;
                case 2004:this.RestartResponse(messageReceivedEventArgs.Data);break;
                case 2005:this.LoadResponse(messageReceivedEventArgs.Data);break;
                case 2006:this.RemoveResponse(messageReceivedEventArgs.Data);break;
                case 2007:this.LogsResponse(messageReceivedEventArgs.Data);break;
                case 2008:this.CommandlineResponse(messageReceivedEventArgs.Data);break;
                //
                case 2101:this.StatusAllResponse(messageReceivedEventArgs.Data);break;
                case 2102:this.StartAllResponse(messageReceivedEventArgs.Data);break;
                case 2103:this.StopAllResponse(messageReceivedEventArgs.Data);break;
                case 2104:this.RestartAllResponse(messageReceivedEventArgs.Data);break;
                case 2105:this.LoadAllResponse(messageReceivedEventArgs.Data);break;
                case 2106:this.RemoveAllResponse(messageReceivedEventArgs.Data);break;
                //
                case 2200:this.DaemonVersionResponse(messageReceivedEventArgs.Data);break;
                case 2201:this.DaemonStatusResponse(messageReceivedEventArgs.Data);break;
                case 2299:this.DaemonShutdownResponse(messageReceivedEventArgs.Data);break;
                //
                case 3002:this.StartNotify(messageReceivedEventArgs.Data);break;
                case 3003:this.StopNotify(messageReceivedEventArgs.Data);break;
                case 3007:this.LogsNotify(messageReceivedEventArgs.Data);break;
                default:break;
            }
        }

        #region 流程
        /// <summary>
        /// 定时发送心跳包
        /// </summary>
        private void ClientKeepAlive() {
            if(!this.Client.Connected){return;}
            ClientKeepAliveProtobuf clientKeepAliveProtobuf=new ClientKeepAliveProtobuf{Type=1};
            this.Client.SendAsync(clientKeepAliveProtobuf.ToByteArray()).Wait();
        }
        /// <summary>
        /// 客户端向服务端请求验证ControlKey
        /// </summary>
        private void ClientOfferControlKey(){
            if(!this.Client.Connected){return;}
            LoggerModuleHelper.TryLog("Modules.WebSocketControlModule.ClientOfferControlKey","向服务端请求验证");
            ClientOfferControlKeyProtobuf clientOfferControlKeyProtobuf=new ClientOfferControlKeyProtobuf{
                Type=12,ConnectionId=this.ClientConnectionId,ControlKey=this.ControlKey,SupportNotify=true};
            _=this.Client.SendAsync(clientOfferControlKeyProtobuf.ToByteArray());
        }
        /// <summary>
        /// 服务端响应客户端链接事件,并回复给客户端
        /// </summary>
        /// <param name="bytes"></param>
        private void ServerAcceptConnection(Byte[] bytes) {
            //解析数据
            ServerAcceptConnectionProtobuf serverAcceptConnectionProtobuf;
            try {
                serverAcceptConnectionProtobuf=ServerAcceptConnectionProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.ServerAcceptConnection[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //设置客户端链接Id
            LoggerModuleHelper.TryLog(
                "Modules.WebSocketControlModule.ServerAcceptConnection",$"已收到客户端链接Id {serverAcceptConnectionProtobuf.ConnectionId}");
            this.ClientConnectionId=serverAcceptConnectionProtobuf.ConnectionId;
            //请求验证客户端
            this.ClientOfferControlKey();
        }
        /// <summary>
        /// 服务端回复客户端ControlKey验证结果
        /// </summary>
        /// <param name="bytes"></param>
        private void ServerValidateConnection(Byte[] bytes){
            //解析数据
            ServerValidateConnectionProtobuf serverValidateConnectionProtobuf;
            try {
                serverValidateConnectionProtobuf=ServerValidateConnectionProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.ServerValidateConnection[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //验证结果
            LoggerModuleHelper.TryLog(
                "Modules.WebSocketControlModule.ServerValidateConnection",$"已收到客户端验证结果 {serverValidateConnectionProtobuf.Valid}");
            this.ClientConnectionValid=serverValidateConnectionProtobuf.Valid;//用于解除自旋锁
        }
        #endregion

        #region 客户端请求
        /// <summary>
        /// windctl status unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void StatusRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StatusRequestProtobuf statusRequestProtobuf=new StatusRequestProtobuf{Type=1001,UnitKey=unitKey};
            _=this.Client.SendAsync(statusRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl start unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void StartRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StartRequestProtobuf startRequestProtobuf=new StartRequestProtobuf{Type=1002,UnitKey=unitKey};
            _=this.Client.SendAsync(startRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl stop unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void StopRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StopRequestProtobuf stopRequestProtobuf=new StopRequestProtobuf{Type=1003,UnitKey=unitKey};
            _=this.Client.SendAsync(stopRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl restart unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void RestartRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            RestartRequestProtobuf restartRequestProtobuf=new RestartRequestProtobuf{Type=1004,UnitKey=unitKey};
            _=this.Client.SendAsync(restartRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl load unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void LoadRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            LoadRequestProtobuf loadRequestProtobuf=new LoadRequestProtobuf{Type=1005,UnitKey=unitKey};
            _=this.Client.SendAsync(loadRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl remove unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void RemoveRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            RemoveRequestProtobuf removeRequestProtobuf=new RemoveRequestProtobuf{Type=1006,UnitKey=unitKey};
            _=this.Client.SendAsync(removeRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl logs unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void LogsRequest(String unitKey) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            LogsRequestProtobuf logsRequestProtobuf=new LogsRequestProtobuf{Type=1007,UnitKey=unitKey,Line=64};
            _=this.Client.SendAsync(logsRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl attach unitKey
        /// </summary>
        /// <param name="unitKey"></param>
        public void CommandlineRequest(String unitKey,Int32 commandType,String commandLine) {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            CommandlineRequestProtobuf commandlineRequestProtobuf=new CommandlineRequestProtobuf{Type=1008,UnitKey=unitKey,CommandType=commandType};
            switch(commandlineRequestProtobuf.CommandType){
                case 1:commandlineRequestProtobuf.CommandLine=commandLine;break;
            }
            _=this.Client.SendAsync(commandlineRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        ////////////////////////////////////////////////////////////////
        /// <summary>
        /// windctl status-all
        /// </summary>
        public void StatusAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StatusAllRequestProtobuf statusAllRequestProtobuf=new StatusAllRequestProtobuf{Type=1101};
            _=this.Client.SendAsync(statusAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl start-all
        /// </summary>
        public void StartAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StartAllRequestProtobuf startAllRequestProtobuf=new StartAllRequestProtobuf{Type=1102};
            _=this.Client.SendAsync(startAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl stop-all
        /// </summary>
        public void StopAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            StopAllRequestProtobuf stopAllRequestProtobuf=new StopAllRequestProtobuf{Type=1103};
            _=this.Client.SendAsync(stopAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl restart-all
        /// </summary>
        public void RestartAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            RestartAllRequestProtobuf restartAllRequestProtobuf=new RestartAllRequestProtobuf{Type=1104};
            _=this.Client.SendAsync(restartAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl load-all
        /// </summary>
        public void LoadAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            LoadAllRequestProtobuf loadAllRequestProtobuf=new LoadAllRequestProtobuf{Type=1105};
            _=this.Client.SendAsync(loadAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl remove-all
        /// </summary>
        public void RemoveAllRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            RemoveAllRequestProtobuf removeAllRequestProtobuf=new RemoveAllRequestProtobuf{Type=1106};
            _=this.Client.SendAsync(removeAllRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        ////////////////////////////////////////////////////////////////
        /// <summary>
        /// windctl daemon-version
        /// </summary>
        public void DaemonVersionRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            DaemonVersionRequestProtobuf daemonVersionRequestProtobuf=new DaemonVersionRequestProtobuf{Type=1200};
            _=this.Client.SendAsync(daemonVersionRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl daemon-status
        /// </summary>
        public void DaemonStatusRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            DaemonStatusRequestProtobuf daemonStatusRequestProtobuf=new DaemonStatusRequestProtobuf{Type=1201};
            _=this.Client.SendAsync(daemonStatusRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        /// <summary>
        /// windctl daemon-shutdown
        /// </summary>
        public void DaemonShutdownRequest() {
            if(!this.Client.Connected || !this.ClientConnectionValid || Program.InAction){return;}
            DaemonShutdownRequestProtobuf daemonShutdownRequestProtobuf=new DaemonShutdownRequestProtobuf{Type=1299};
            _=this.Client.SendAsync(daemonShutdownRequestProtobuf.ToByteArray());
            Program.InAction=true;
        }
        #endregion

        #region 服务端响应
        /// <summary>
        /// windctl status unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void StatusResponse(Byte[] bytes) {
            //解析数据
            StatusResponseProtobuf statusResponseProtobuf;
            try {
                statusResponseProtobuf=StatusResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StatusResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Status(statusResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl start unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void StartResponse(Byte[] bytes) {
            //解析数据
            StartResponseProtobuf startResponseProtobuf;
            try {
                startResponseProtobuf=StartResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StartResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Start(startResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl start unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void StopResponse(Byte[] bytes) {
            //解析数据
            StopResponseProtobuf stopResponseProtobuf;
            try {
                stopResponseProtobuf=StopResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StopResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Stop(stopResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl restart unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void RestartResponse(Byte[] bytes) {
            //解析数据
            RestartResponseProtobuf restartResponseProtobuf;
            try {
                restartResponseProtobuf=RestartResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.RestartResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Restart(restartResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl load unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void LoadResponse(Byte[] bytes) {
            //解析数据
            LoadResponseProtobuf loadResponseProtobuf;
            try {
                loadResponseProtobuf=LoadResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.LoadResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Load(loadResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl remove unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void RemoveResponse(Byte[] bytes) {
            //解析数据
            RemoveResponseProtobuf removeResponseProtobuf;
            try {
                removeResponseProtobuf=RemoveResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.RemoveResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Remove(removeResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl logs unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void LogsResponse(Byte[] bytes) {
            //解析数据
            LogsResponseProtobuf logsResponseProtobuf;
            try {
                logsResponseProtobuf=LogsResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.LogsResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Logs(logsResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl attach unitKey
        /// </summary>
        /// <param name="bytes"></param>
        private void CommandlineResponse(Byte[] bytes) {
            //解析数据
            CommandlineResponseProtobuf commandlineResponseProtobuf;
            try {
                commandlineResponseProtobuf=CommandlineResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.AttachResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.Commandline(commandlineResponseProtobuf);
            Program.InAction=false;
        }
        ////////////////////////////////////////////////////////////////
        /// <summary>
        /// windctl status-all
        /// </summary>
        /// <param name="bytes"></param>
        private void StatusAllResponse(Byte[] bytes) {
            //解析数据
            StatusAllResponseProtobuf statusAllResponseProtobuf;
            try {
                statusAllResponseProtobuf=StatusAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StatusAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.StatusAll(statusAllResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl start-all
        /// </summary>
        /// <param name="bytes"></param>
        private void StartAllResponse(Byte[] bytes) {
            //解析数据
            StartAllResponseProtobuf startAllResponseProtobuf;
            try {
                startAllResponseProtobuf=StartAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StartAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.StartAll(startAllResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl stop-all
        /// </summary>
        /// <param name="bytes"></param>
        private void StopAllResponse(Byte[] bytes) {
            //解析数据
            StopAllResponseProtobuf stopAllResponseProtobuf;
            try {
                stopAllResponseProtobuf=StopAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StopAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.StopAll(stopAllResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl restart-all
        /// </summary>
        /// <param name="bytes"></param>
        private void RestartAllResponse(Byte[] bytes) {
            //解析数据
            RestartAllResponseProtobuf restartAllResponseProtobuf;
            try {
                restartAllResponseProtobuf=RestartAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.RestartAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.RestartAll(restartAllResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl load-all
        /// </summary>
        /// <param name="bytes"></param>
        private void LoadAllResponse(Byte[] bytes) {
            //解析数据
            LoadAllResponseProtobuf loadAllResponseProtobuf;
            try {
                loadAllResponseProtobuf=LoadAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.LoadAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.LoadAll(loadAllResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl remove-all
        /// </summary>
        /// <param name="bytes"></param>
        private void RemoveAllResponse(Byte[] bytes) {
            //解析数据
            RemoveAllResponseProtobuf removeAllResponseProtobuf;
            try {
                removeAllResponseProtobuf=RemoveAllResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.RemoveAllResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.RemoveAll(removeAllResponseProtobuf);
            Program.InAction=false;
        }
        ////////////////////////////////////////////////////////////////
        /// <summary>
        /// windctl daemon-version
        /// </summary>
        /// <param name="bytes"></param>
        private void DaemonVersionResponse(Byte[] bytes) {
            //解析数据
            DaemonVersionResponseProtobuf daemonVersionResponseProtobuf;
            try {
                daemonVersionResponseProtobuf=DaemonVersionResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.DaemonVersionResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.DaemonVersion(daemonVersionResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl daemon-status
        /// </summary>
        /// <param name="bytes"></param>
        private void DaemonStatusResponse(Byte[] bytes) {
            //解析数据
            DaemonStatusResponseProtobuf daemonStatusResponseProtobuf;
            try {
                daemonStatusResponseProtobuf=DaemonStatusResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.DaemonStatusResponse[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.DaemonStatus(daemonStatusResponseProtobuf);
            Program.InAction=false;
        }
        /// <summary>
        /// windctl daemon-shutdown
        /// </summary>
        /// <param name="bytes"></param>
        private void DaemonShutdownResponse(Byte[] bytes) {
            //解析数据
            DaemonShutdownResponseProtobuf daemonShutdownResponseProtobuf;
            try {
                daemonShutdownResponseProtobuf=DaemonShutdownResponseProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.DaemonShutdown[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            CommandHelper.DaemonShutdown(daemonShutdownResponseProtobuf);
            Program.InAction=false;
        }
        #endregion

        #region 服务端通知
        /// <summary>
        /// StartNotify
        /// </summary>
        /// <param name="bytes"></param>
        private void StartNotify(Byte[] bytes) {
            //解析数据
            StartNotifyProtobuf startNotifyProtobuf;
            try {
                startNotifyProtobuf=StartNotifyProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StartNotify[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            NotifyHelper.Start(startNotifyProtobuf);
        }
        /// <summary>
        /// StopNotify
        /// </summary>
        /// <param name="bytes"></param>
        private void StopNotify(Byte[] bytes) {
            //解析数据
            StopNotifyProtobuf stopNotifyProtobuf;
            try {
                stopNotifyProtobuf=StopNotifyProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.StopNotify[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            NotifyHelper.Stop(stopNotifyProtobuf);
        }
        /// <summary>
        /// LogsNotify
        /// </summary>
        /// <param name="bytes"></param>
        private void LogsNotify(Byte[] bytes) {
            //解析数据
            LogsNotifyProtobuf logsNotifyProtobuf;
            try {
                logsNotifyProtobuf=LogsNotifyProtobuf.Parser.ParseFrom(bytes);
            }catch(Exception exception){
                LoggerModuleHelper.TryLog(
                    "Modules.WebSocketControlModule.LogsNotify[Error]",
                    $"解析数据包时异常,{exception.Message}\n异常堆栈:{exception.StackTrace}");
                return;
            }
            //调用
            NotifyHelper.Logs(logsNotifyProtobuf);
        }
        #endregion
    }
}
