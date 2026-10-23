package skua.api {
import flash.display.MovieClip;
import flash.events.Event;
import flash.events.MouseEvent;
import flash.events.TimerEvent;
import flash.system.Security;
import flash.utils.Timer;

import skua.Main;
import skua.module.ModalMC;

public class Server {

    public function Server() {
        super();
    }

    public static function loadPolicyFiles():void {
        try {
            if (Main.instance.game == null) return;

            // 1. Check serialCmd.servers
            if (Main.instance.game.serialCmd != null && Main.instance.game.serialCmd.servers != null) {
                var servers:Array = Main.instance.game.serialCmd.servers as Array;
                if (servers != null) {
                    for (var i:int = 0; i < servers.length; i++) {
                        var s:Object = servers[i];
                        if (s != null && s.sIP != null && s.iPort != null) {
                            var port:int = int(s.iPort);
                            if (port > 0) {
                                Security.loadPolicyFile("xmlsocket://" + String(s.sIP) + ":" + port);
                            }
                        }
                    }
                }
            }

            // 2. Check mcLogin.sl.iList children
            if (Main.instance.game.mcLogin != null && Main.instance.game.mcLogin.sl != null && Main.instance.game.mcLogin.sl.iList != null) {
                var source:* = Main.instance.game.mcLogin.sl.iList;
                for (var j:int = 0; j < source.numChildren; j++) {
                    var child:* = source.getChildAt(j);
                    if (child != null) {
                        var cServer:Object = child.server || child.objServer;
                        if (cServer != null && cServer.sIP != null && cServer.iPort != null) {
                            var cPort:int = int(cServer.iPort);
                            if (cPort > 0) {
                                Security.loadPolicyFile("xmlsocket://" + String(cServer.sIP) + ":" + cPort);
                            }
                        }
                    }
                }
            }
        } catch (e:Error) {
            Main.instance.external.debug("Error in loadPolicyFiles: " + e);
        }
    }

    public static function loadPolicyFile(ip:String, port:int):void {
        try {
            if (ip != null && ip.length > 0 && port > 0) {
                Security.loadPolicyFile("xmlsocket://" + ip + ":" + port);
            }
        } catch (e:Error) {
            Main.instance.external.debug("Error in loadPolicyFile: " + e);
        }
    }

    public static function clickServer(serverName:String):String {
        loadPolicyFiles();
        var source:* = Main.instance.game.mcLogin.sl.iList;
        for (var i:int = 0; i < source.numChildren; i++) {
            var child:* = source.getChildAt(i);

            if (child.tName.ti.text.toLowerCase().indexOf(serverName.toLowerCase()) > -1) {
                try {
                    var cServer:Object = child.server || child.objServer;
                    if (cServer != null && cServer.sIP != null && cServer.iPort != null) {
                        Security.loadPolicyFile("xmlsocket://" + String(cServer.sIP) + ":" + int(cServer.iPort));
                    }
                } catch (e:Error) {}
                child.dispatchEvent(new MouseEvent(MouseEvent.CLICK));
                return true.toString();
            }
        }
        return false.toString();
    }

    public static function connectToServer(server:String):String {
        var serverData:Object = JSON.parse(server);
        if (serverData != null && serverData.sIP != null && serverData.iPort != null) {
            try {
                Security.loadPolicyFile("xmlsocket://" + String(serverData.sIP) + ":" + int(serverData.iPort));
            } catch (e:Error) {}
        }

        var objLoginStr:String = Main.getGameObjectS("objLogin");
        var objLogin:Object = (objLoginStr != null && objLoginStr.length > 0) ? JSON.parse(objLoginStr) : null;
        if (objLogin != null) {
            connectServer(serverData, objLogin);
            return true.toString();
        }

        var connectionServerTimer:Timer = new Timer(100, 50);
        connectionServerTimer.addEventListener(TimerEvent.TIMER, connectingServer);
        connectionServerTimer.start();

        function connectingServer(e:Event):void {
            objLoginStr = Main.getGameObjectS("objLogin");
            objLogin = (objLoginStr != null && objLoginStr.length > 0) ? JSON.parse(objLoginStr) : null;
            if (objLogin != null) {
                connectServer(serverData, objLogin);
                connectionServerTimer.stop();
                connectionServerTimer.removeEventListener(TimerEvent.TIMER, connectingServer);
            }
        }

        return true.toString();
    }

    private static function connectServer(server:Object, objLoginData:Object):* {
        Main.instance.game.showTracking("4");
        if (Main.instance.game.serialCmdMode) {
            return;
        }

        if (server.bOnline == 0) {
            Main.instance.game.MsgBox.notify("Server currently offline!");
        } else if (server.iCount >= server.iMax) {
            Main.instance.game.MsgBox.notify("Server is Full!");
        } else if (server.iChat > 0 && objLoginData.bCCOnly == 1) {
            Main.instance.game.MsgBox.notify("Account Restricted to Moglin Sage Server Only.");
        } else if (server.iChat > 0 && objLoginData.iAge < 13 && objLoginData.iUpgDays < 0) {
            Main.instance.game.MsgBox.notify("Ask your parent to upgrade your account in order to play on chat enabled servers.");
        } else if (server.bUpg == 1 && objLoginData.iUpgDays < 0) {
            showModal("Member Server! Do you want to upgrade your account to access this premium server now?", "white,medium", "dual");
        } else if (Number(server.iMax) % 2 > 0) {
            showModal("Testing Server! Do you want to switch to the testing game client?", "white,medium", "dual");
        } else if (server.iLevel > 0 && objLoginData.iEmailStatus <= 2) {
            showModal("This server requires a confirmed email address.", "red,medium", "mono");
        } else {
            if (server != null && server.sIP != null && server.iPort != null) {
                try {
                    Security.loadPolicyFile("xmlsocket://" + String(server.sIP) + ":" + int(server.iPort));
                } catch (e:Error) {}
            }
            Main.instance.game.objServerInfo = server;
            Main.instance.game.chatF.iChat = server.iChat;
            killLoginModals();
            Main.instance.game.connectTo(server.sIP, server.iPort);
        }
    }

    private static function showModal(message:String, glow:String, buttons:String):void {
        var modal:ModalMC = new ModalMC();
        var params:Object = {
            strBody: message,
            params: {},
            glow: glow,
            btns: buttons
        };
        Main.instance.game.mcLogin.ModalStack.addChild(modal);
        modal.init(params);
    }

    private static function killLoginModals():void {
        var modalStack:MovieClip = Main.instance.game.mcLogin.ModalStack;
        for (var i:int = 0; i < modalStack.numChildren; i++) {
            var modal:MovieClip = modalStack.getChildAt(i) as MovieClip;
            if (modal && "fClose" in modal) {
                modal.fClose();
            }
        }
    }
}
}
