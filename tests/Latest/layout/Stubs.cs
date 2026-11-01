using System.Reflection;
namespace UnityEngine
{
 public class Object { }
 public struct Vector2(float x,float y) {public float x=x,y=y;}
 public class Component:Object {public GameObject gameObject=null!;public Transform transform=>gameObject.transform;}
 public class Behaviour:Component {public bool enabled{get;set;}=true;}
 public class Canvas:Behaviour { }
 public class Transform:Component {public Transform? parent;}
 public class RectTransform:Transform
 {
  public enum Axis {Horizontal,Vertical}
  public Vector2 anchorMin,anchorMax,sizeDelta;
  public float Width=>sizeDelta.x+(parent is RectTransform r?r.Width:0)*(anchorMax.x-anchorMin.x);
  public float Height=>sizeDelta.y+(parent is RectTransform r?r.Height:0)*(anchorMax.y-anchorMin.y);
  public void SetSizeWithCurrentAnchors(Axis axis,float size)
  {if(axis==Axis.Horizontal)sizeDelta.x=size-(parent is RectTransform rw?rw.Width:0)*(anchorMax.x-anchorMin.x);else sizeDelta.y=size-(parent is RectTransform rh?rh.Height:0)*(anchorMax.y-anchorMin.y);}
 }
 public class GameObject:Object
 {
  public readonly List<Component> Components=new();public readonly RectTransform transform;
  public GameObject(){transform=new RectTransform{gameObject=this};Components.Add(transform);}
  public Component? GetComponent(Type t)=>Components.FirstOrDefault(c=>t.IsInstanceOfType(c));
  public Component AddComponent(Type t){var c=(Component)Activator.CreateInstance(t)!;c.gameObject=this;Components.Add(c);return c;}
 }
}
namespace UnityEngine.UI
{
 public class LayoutElement:UnityEngine.Behaviour
 {
  public bool ignoreLayout{get;set;}public int layoutPriority{get;set;}=1;
  public float minWidth{get;set;}=-1;public float preferredWidth{get;set;}=-1;public float flexibleWidth{get;set;}=-1;
  public float minHeight{get;set;}=-1;public float preferredHeight{get;set;}=-1;public float flexibleHeight{get;set;}=-1;
 }
 public static class LayoutRebuilder
 {public static readonly HashSet<UnityEngine.RectTransform> Dirty=new();public static void MarkLayoutForRebuild(UnityEngine.RectTransform rect)=>Dirty.Add(rect);}
}
namespace HarmonyLib
{
 public static class AccessTools
 {
  public static Type TypeByName(string name)=>typeof(AccessTools).Assembly.GetType(name,true)!;
  public static MethodInfo Method(Type t,string name,Type[] types)=>t.GetMethod(name,types)!;
 }
}
namespace EscapeFromBorderlands.Client
{
 internal static class Native
 {internal static void Set(object target,string name,object value)=>target.GetType().GetProperty(name)!.SetValue(target,value);}
}
