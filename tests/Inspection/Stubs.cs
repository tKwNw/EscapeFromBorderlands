using System.Reflection;
namespace EFT.InventoryLogic {
 public class Item { public string Id=""; public string StringTemplateId=""; }
 public class ItemAddress { }
}
namespace EFT.UI {
 public class TextControl { public string text {get;set;}=""; }
 public class ItemInfoWindowLabels {
  private readonly TextControl _caption=new();
  public string Caption=>_caption.text;
  public string Category="WEAPON > PISTOL", Description="blablabla", Size="OK";
  public void SetCaptionText(string value)=>_caption.text=value;
  public void SetDescriptionText(string value)=>Description=value;
 }
 public class ItemSpecificationPanel {
  private EFT.InventoryLogic.Item? _item;
  private ItemInfoWindowLabels? _itemLabels;
  public bool ThrowGetter, IsExamined=true;
  // SPT 4.1.6 has Examined and does not have Boolean_0.
  private bool Examined=>ThrowGetter?throw new InvalidOperationException("fixture getter failure"):IsExamined;
  public ItemSpecificationPanel(EFT.InventoryLogic.Item? item,ItemInfoWindowLabels? labels){_item=item;_itemLabels=labels;}
 }
}
namespace EFT.UI.DragAndDrop { public class GridItemView { public EFT.InventoryLogic.Item Item=new(); } }
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)] public sealed class HarmonyPatch:Attribute {
  public HarmonyPatch(){} public HarmonyPatch(Type t,string m,Type[] p){}
 }
 public static class AccessTools {
  const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,All)!;
  public static PropertyInfo Property(Type t,string n)=>t.GetProperty(n,All)!;
  public static MethodInfo Method(Type t,string n,Type[] p)=>t.GetMethod(n,All,null,p,null)!;
 }
}
namespace EscapeFromBorderlands.Client {
 internal static class Plugin {
  internal static readonly Log LogSource=new();
  internal sealed class Log {
   internal readonly List<string> Warnings=new();
   public void LogInfo(string text){} public void LogWarning(object text)=>Warnings.Add(text.ToString()!);
  }
 }
}
