// Minimal engine boundary for the production quest presentation adapters.
// The production build and native metadata audit use the real game types.
namespace EFT
{
    public readonly record struct MongoID(string Value)
    {
        public static implicit operator MongoID(string value) => new(value);
        public override string ToString() => Value;
    }
}
namespace EFT.InventoryLogic
{
    public partial class Item { public EFT.MongoID TemplateId => StringTemplateId; }
    public partial class Inventory { public List<string> EquippedInSlotsTemplateIds => []; public static bool IsWeaponFitsCondition(Weapon weapon,EFT.Quests.ConditionWeaponAssembly condition,bool displayLog=false)=>false; }
}
namespace EFT.Quests
{
    public class ConditionItem { public string[] target=[]; }
    public class ConditionFindItem : ConditionItem { public bool TargetIsCategory; }
    public class ConditionHandoverItem : ConditionItem { }
    public class ConditionWeaponAssembly : ConditionItem { }
    public class ConditionHit { public string[]? weapon; }
}
namespace EFT.HandBook { public interface IBaseHandbook { bool IsChildOf(string item, string parent); } }

public class Quest { }
public class ConditionsConnectorsManager<T> { public static bool ContainsPass(EFT.InventoryLogic.Item item, EFT.Quests.ConditionItem condition, EFT.HandBook.IBaseHandbook handbook) => false; }
public class ConditionalController<T> { public void IncreaseSellToTraderCounter(string traderId,string templateId,int count) { } }
public class HitConditionCheck
{
    public string Weapon="",TargetSide="",ScavRole="";
    public string[]? WeaponMods=[];
    public List<string>? TargetEquipment=[];
    public EFT.EBodyPart BodyPart;
    public float Distance;
    public int CurrentHour,Value;
    public object? EnemyHealthEffects;
}
public class ConditionHitProgressChecker { public EFT.Quests.ConditionHit Condition=new(); public bool Test(object testValue)=>false; }
public class ConditionEquipmentProgressChecker { public bool Test(object testValue)=>false; }
public class ConditionUseItemProgressChecker { public bool Test(object testValue)=>false; }
namespace EFT.UI.DragAndDrop { public class QuestItemViewPanel { public static bool smethod_0()=>false; } }
namespace EFT.InventoryLogic { public class QuestHandoverResult { } }
namespace Diz.LanguageExtensions { public class StringError(string message) { public string Error=message; public override string ToString()=>Error; }
public readonly record struct OperationResult<T>(StringError? Error)
{
    public static implicit operator OperationResult<T>(StringError error)=>new(error);
}
}
namespace EFT.InventoryLogic { public class ItemManipulator { public static Diz.LanguageExtensions.OperationResult<QuestHandoverResult> HandoverQuest(Item[] items)=>default; } }
namespace EFT { public class InteractionContextHelper
{
    public class CG_GetAvailableActions5 { public EFT.InventoryLogic.Item resultItem=null!; public void method_0(bool successful) { } }
}

}
