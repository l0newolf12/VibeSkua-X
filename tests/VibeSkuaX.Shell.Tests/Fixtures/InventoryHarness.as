package {
import flash.display.MovieClip;
import flash.external.ExternalInterface;
import flash.system.ApplicationDomain;
import skua.Main;
import skua.api.Inventory;
import skua.api.Player;

public class InventoryHarness extends MovieClip {
    // Link the supplied game's category implementation, not a reimplementation.
    private var categoryClass:Class = InvCat;
    private var bridge:Main;
    public function InventoryHarness() {
        bridge = new Main();
        ExternalInterface.addCallback("runInventoryTests", run);
        ExternalInterface.addCallback("nativeTrue", function():Boolean { return true; });
        ExternalInterface.addCallback("nativeNumber", function():Number { return 1.5; });
        ExternalInterface.addCallback("nativeNull", function():* { return null; });
        ExternalInterface.addCallback("isLoggedIn", Player.isLoggedIn);
        ExternalInterface.addCallback("getGameObject", Main.getGameObject);
        ExternalInterface.addCallback("setGameObject", Main.setGameObject);
        ExternalInterface.call("loaded", []);
    }
    private function run():String {
        var checks:Array = [];
        var avatar:Object = { items: [], houseitems: [], objData: { iBagSlots: 2, iHouseSlots: 1 } };
        bridge.game = { loaderInfo: { applicationDomain: ApplicationDomain.currentDomain }, world: { myAvatar: avatar } };
        checks.push({ name: "category capacity", pass: Inventory.inventoryMiscSlots() == 2 });
        var sword:Object = { ItemID: 1, sType: "Sword", iQty: 1, iStk: 1 };
        var helm:Object = { ItemID: 2, sType: "Helm", iQty: 1, iStk: 1 };
        var note:Object = { ItemID: 3, sType: "Note", iQty: 1, iStk: 10 };
        var resource:Object = { ItemID: 4, sType: "Resource", iQty: 1, iStk: 1 };
        avatar.items = [sword, helm];
        checks.push({ name: "independent misc pool", pass: Inventory.inventoryHasSpaceFor(JSON.stringify(note)) == "true" });
        checks.push({ name: "full bag", pass: Inventory.inventoryHasSpaceFor(JSON.stringify({ ItemID: 5, sType: "Sword" })) == "false" });
        checks.push({ name: "same-pool bank swap", pass: Inventory.inventoryHasSpaceFor(JSON.stringify({ ItemID: 5, sType: "Sword" }), 1, false, JSON.stringify(sword)) == "true" });
        avatar.items = [note, resource];
        checks.push({ name: "partial stack", pass: Inventory.inventoryHasSpaceFor(JSON.stringify(note)) == "true" });
        checks.push({ name: "full misc pool", pass: Inventory.inventoryHasSpaceFor(JSON.stringify({ ItemID: 6, sType: "Note" })) == "false" });
        checks.push({ name: "misc count", pass: Inventory.inventoryMiscUsedSlots() == 2 && Inventory.inventoryBagUsedSlots() == 0 });
        bridge.game.loaderInfo.applicationDomain = { hasDefinition: function(name:String):Boolean { return false; } };
        checks.push({ name: "category-less fallback", pass: Inventory.inventoryMiscSlots() == 0 && Inventory.hasInventoryCategories() == "false" });
        return JSON.stringify(checks);
    }
}
}
