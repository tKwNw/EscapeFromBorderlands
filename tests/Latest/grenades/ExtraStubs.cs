using UnityEngine;
namespace HarmonyLib
{
    public static class Priority { public const int Last=0, First=800; }
    public class HarmonyMethod(Type type,string name)
    {
        public int priority;
        public System.Reflection.MethodInfo Method=AccessTools.Method(type,name);
    }
    public class Harmony(string id)
    {
        public static int Registrations;
        public static System.Reflection.MethodInfo? Target;
        public static HarmonyMethod? Postfix;
        public void Patch(System.Reflection.MethodInfo original,HarmonyMethod postfix)
        { _=id; Registrations++;Target=original;Postfix=postfix; }
    }
}
namespace EFT.InventoryLogic
{
    public enum EItemAttributeId { ExplosionDelay, MaximumThrowDamage, ExplosionDistance, FragmentsCount, PyrotechnicDelay }
    public enum EItemAttributeDisplayType { Compact }
    public class ItemTemplate { }
    public class AmmoTemplate : ItemTemplate { public int Damage; }
    public partial class Item { public List<ItemAttribute> Attributes = []; }
    public interface IExplosiveItem
    {
        Vector3 Blindness { get; } Vector3 Contusion { get; } Vector3 ArmorDistanceDistanceDamage { get; }
        float MinExplosionDistance { get; } float MaxExplosionDistance { get; } int FragmentsCount { get; }
        float GetStrength { get; } bool IsDummy { get; } Ammo CreateFragment();
    }
}
public class ItemAttribute(Enum id)
{
    public Enum Id = id;
    public string Name = "";
    public bool LessIsGood;
    public Func<float> Base = () => 0;
    public Func<string> StringValue = () => "", Tooltip = () => "";
    public Func<EFT.InventoryLogic.EItemAttributeDisplayType> DisplayType = () => EFT.InventoryLogic.EItemAttributeDisplayType.Compact;
}
public static class LocalizationExtensions { public static string Localized(string text) => text; }
public class Ammo : EFT.InventoryLogic.Item { }
public interface IHandsController { }
public class InventoryExtension
{
    public static List<ThrowWeap> GetThrowablePriorityGrenadesList(EFT.InventoryLogic.InventoryController inventoryController)
    {
        var list=inventoryController.Reachable.OfType<ThrowWeap>().Where(i=>i.Examined).ToList();
        object?[] args=[list];typeof(EscapeFromBorderlands.Client.ReusableGrenadeQuickSelectPatch)
            .GetMethod("Postfix",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,args);
        return (List<ThrowWeap>)args[0]!;
    }
}
public enum ThrowWeapType {frag_grenade, flash_grenade}
public class BotGrenadeController
{
    public bool HaveGrenadeOfType(ThrowWeapType type)=>false;
    public int GetGrenadeCount()=>0;
    public void CheckGrenadeWithType(ThrowWeapType type) { }
    public void CheckGrenade() { }
}
public class BotInventorySource
{
    public IEnumerable<EFT.InventoryLogic.Item> GetPlayerItems(int kinds)=>[];
}
namespace EscapeFromBorderlands.Client
{
    internal static class ChestRigCombat
    {
        internal static bool RigBonus;
        internal static bool GrenadeBonus(EFT.Player.BaseGrenadeHandsController controller, string key) => RigBonus && key == "TR07-2";
    }
}
namespace EFT.UI
{
    public class StaticIcons { public object GetAttributeIcon(Enum id) => id; }
    public class ItemInfoWindowLabels { public void SetDescriptionText(string value) { } }
    public class ItemSpecificationPanel { public EFT.InventoryLogic.Item? _item; public ItemInfoWindowLabels? _itemLabels; public bool Examined => true; public void method_1() { } public void RecreateAttributeBars() { } public void Compare(EFT.InventoryLogic.Item compareItem) { } }
}
