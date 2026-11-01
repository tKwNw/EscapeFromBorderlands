using System.Reflection;
namespace UnityEngine {
 public class Object {
  internal bool Destroyed;
  public static void Destroy(Object? obj){if(ReferenceEquals(obj,null))return;obj.Destroyed=true;if(obj is GameObject go){foreach(var child in go.transform.Children.ToArray())Destroy(child.gameObject);foreach(var c in go.Components)c.Destroyed=true;go.transform.parent=null;}}
  public static bool operator ==(Object? a,Object? b)=>ReferenceEquals(a,b)||((ReferenceEquals(a,null)||a.Destroyed)&&(ReferenceEquals(b,null)||b.Destroyed));
  public static bool operator !=(Object? a,Object? b)=>!(a==b);
  public override bool Equals(object? b)=>ReferenceEquals(this,b);public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
 }
 public struct Vector2(float x,float y){public float x=x,y=y;public static Vector2 zero=>new(0,0);public static Vector2 one=>new(1,1);}
 public struct Vector3(float x,float y,float z=0){public float x=x,y=y,z=z;public static Vector3 one=>new(1,1,1);}
 public struct Color(float r,float g,float b,float a){public float r=r,g=g,b=b,a=a;public static Color white=>new(1,1,1,1);}
 public struct Rect(float width,float height,Vector2 pivot){public float width=width,height=height;public float xMin=>-width*pivot.x;public float yMin=>-height*pivot.y;}
 public class Component:Object {public GameObject gameObject=null!;public Transform transform=>gameObject.transform;public T[] GetComponents<T>() where T:Component=>gameObject.Components.OfType<T>().ToArray();public Component[] GetComponentsInChildren(Type type,bool includeInactive)=>gameObject.Walk().Where(g=>includeInactive||g.activeInHierarchy).SelectMany(g=>g.Components).Where(c=>type.IsInstanceOfType(c)).ToArray();}
 public class Behaviour:Component {public bool enabled{get;set;}=true;}
 public class Canvas:Behaviour {}
 public class Transform:Component {
  internal readonly List<Transform> Children=[];private Transform? Parent;
  public Transform? parent {get=>Parent;set{Parent?.Children.Remove(this);Parent=value;value?.Children.Add(this);}}
  public Vector3 localScale=Vector3.one;
  public void SetParent(Transform? p,bool worldPositionStays)=>parent=p;
  public void SetSiblingIndex(int i){if(parent is not {} p)return;p.Children.Remove(this);p.Children.Insert(Math.Min(i,p.Children.Count),this);}
  public int GetSiblingIndex()=>parent?.Children.IndexOf(this)??0;
  public bool IsChildOf(Transform p){for(Transform? t=this;t!=null;t=t.parent)if(ReferenceEquals(t,p))return true;return false;}
  internal Vector3 WorldScale {get {var p=parent?.WorldScale??Vector3.one;return new(p.x*localScale.x,p.y*localScale.y,p.z*localScale.z);}}
  internal Vector3 WorldPosition {get {var v=parent?.WorldPosition??new();var scale=parent?.WorldScale??Vector3.one;if(this is RectTransform r){v.x+=r.anchoredPosition.x*scale.x;v.y+=r.anchoredPosition.y*scale.y;}return v;}}
  public Vector3 TransformPoint(Vector3 p){var w=WorldPosition;var s=WorldScale;return new(w.x+p.x*s.x,w.y+p.y*s.y,w.z+p.z*s.z);}
  public Vector3 InverseTransformPoint(Vector3 p){var w=WorldPosition;var s=WorldScale;return new((p.x-w.x)/s.x,(p.y-w.y)/s.y,(p.z-w.z)/s.z);}
  public Vector3 TransformVector(Vector3 p){var s=WorldScale;return new(p.x*s.x,p.y*s.y,p.z*s.z);}
  public Vector3 InverseTransformVector(Vector3 p){var s=WorldScale;return new(p.x/s.x,p.y/s.y,p.z/s.z);}

 }
 public class RectTransform:Transform {
  public enum Axis {Horizontal,Vertical}
  public Vector2 anchorMin,anchorMax,pivot=new(0,1),anchoredPosition,sizeDelta;private Vector2 OffsetMin,OffsetMax;
  public Vector2 offsetMin {get=>OffsetMin;set {sizeDelta.x+=OffsetMin.x-value.x;sizeDelta.y+=OffsetMin.y-value.y;OffsetMin=value;}}
  public Vector2 offsetMax {get=>OffsetMax;set {sizeDelta.x+=value.x-OffsetMax.x;sizeDelta.y+=value.y-OffsetMax.y;OffsetMax=value;}}
  public Rect rect=>new(sizeDelta.x+(parent is RectTransform p?p.rect.width:0)*(anchorMax.x-anchorMin.x),sizeDelta.y+(parent is RectTransform q?q.rect.height:0)*(anchorMax.y-anchorMin.y),pivot);
  public static int SizeWrites;
  public void SetSizeWithCurrentAnchors(Axis axis,float v){SizeWrites++;if(axis==Axis.Horizontal)sizeDelta.x=v-(parent is RectTransform p?p.rect.width:0)*(anchorMax.x-anchorMin.x);else sizeDelta.y=v-(parent is RectTransform q?q.rect.height:0)*(anchorMax.y-anchorMin.y);}
  public void GetWorldCorners(Vector3[] c){var r=rect;c[0]=TransformPoint(new(r.xMin,r.yMin));c[1]=TransformPoint(new(r.xMin,r.yMin+r.height));c[2]=TransformPoint(new(r.xMin+r.width,r.yMin+r.height));c[3]=TransformPoint(new(r.xMin+r.width,r.yMin));}
 }
 public class GameObject:Object {
  public readonly List<Component> Components=[];public readonly RectTransform transform;public string name;public int layer;
  public bool activeSelf{get;private set;}=true;public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public GameObject(string n="",params Type[] types){name=n;transform=new(){gameObject=this};Components.Add(transform);foreach(var t in types.Where(t=>t!=typeof(RectTransform)))AddComponent(t);}
  public void SetActive(bool v)=>activeSelf=v;
  public static int ComponentLookups;
  public Component? GetComponent(Type t){ComponentLookups++;return Components.FirstOrDefault(c=>t.IsInstanceOfType(c));}
  public Component AddComponent(Type t){var c=(Component)Activator.CreateInstance(t)!;c.gameObject=this;Components.Add(c);return c;}
  public IEnumerable<GameObject> Walk(){yield return this;foreach(var child in transform.Children)foreach(var go in child.gameObject.Walk())yield return go;}
 }
}
namespace UnityEngine.UI {
 public class LayoutElement:UnityEngine.Behaviour {public bool ignoreLayout{get;set;}public int layoutPriority{get;set;}public float minWidth{get;set;}public float preferredWidth{get;set;}public float flexibleWidth{get;set;}public float minHeight{get;set;}public float preferredHeight{get;set;}public float flexibleHeight{get;set;}}
 public static class LayoutRebuilder {public static int Marks;public static void MarkLayoutForRebuild(UnityEngine.RectTransform r){Marks++;}}
 public class Graphic:UnityEngine.Behaviour {public UnityEngine.Color color{get;set;}public bool raycastTarget{get;set;}=true;}
 public class Image:Graphic {}
 public class RectMask2D:UnityEngine.Behaviour {}
 public class Scrollbar:UnityEngine.Behaviour {public enum Direction{LeftToRight,RightToLeft,BottomToTop,TopToBottom}public UnityEngine.RectTransform handleRect{get;set;}=null!;public Graphic targetGraphic{get;set;}=null!;public Direction direction{get;set;}}
 public class ScrollRect:UnityEngine.Behaviour {
  public enum ScrollbarVisibility{Permanent,AutoHide,AutoHideAndExpandViewport}public enum MovementType{Unrestricted,Elastic,Clamped}
  public UnityEngine.RectTransform content{get;set;}=null!;public UnityEngine.RectTransform viewport{get;set;}=null!;
  public bool horizontal{get;set;}public bool vertical{get;set;}=true;public Scrollbar? horizontalScrollbar{get;set;}public Scrollbar? verticalScrollbar{get;set;}
  public ScrollbarVisibility horizontalScrollbarVisibility{get;set;}public ScrollbarVisibility verticalScrollbarVisibility{get;set;}public MovementType movementType{get;set;}
  public static int NormalizedWrites;private float HNormalized;public float horizontalNormalizedPosition{get=>HNormalized;set{HNormalized=value;NormalizedWrites++;}}public float verticalNormalizedPosition{get;set;}=1f;public UnityEngine.Vector2 velocity{get;set;}
 }
 public delegate void UnityAction();
 public class Click {private event UnityAction? Listeners;public void AddListener(UnityAction a)=>Listeners+=a;public void Invoke()=>Listeners?.Invoke();}
 public class Button:UnityEngine.Behaviour {public Graphic targetGraphic{get;set;}=null!;public Click onClick{get;}=new();}
}
namespace EFT.UI {public class ScrollRectNoDrag:UnityEngine.UI.ScrollRect{}public class GridWindow:UnityEngine.Behaviour{}}
namespace TMPro {public class TextMeshProUGUI:UnityEngine.UI.Graphic {public enum Alignment{Left,Center}public object? font{get;set;}public object? fontSharedMaterial{get;set;}public string text{get;set;}="";public float fontSize{get;set;}public bool enableAutoSizing{get;set;}public Alignment alignment{get;set;}}}

namespace EscapeFromBorderlands.Client {
 internal static class Native {internal static void Set(object o,string n,object v)=>o.GetType().GetProperty(n)!.SetValue(o,v);}
 internal static class Plugin {internal static readonly Log LogSource=new();internal sealed class Log {internal readonly List<string> Errors=[];internal void LogWarning(string s)=>Errors.Add(s);}}
}

namespace EFT.UI { public static class UIExtensions {
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 public static void KeepChildInScrollViewPort(UnityEngine.UI.ScrollRect scrollRect,UnityEngine.RectTransform child,int moveDirection=0,UnityEngine.Vector2 margin=default){scrollRect.content.anchoredPosition=new(-170,scrollRect.content.anchoredPosition.y-60);scrollRect.velocity=new(50,-10);}
}

}
