using UnityEngine;
using UnityEngine.UI;
using EscapeFromBorderlands.Client;
RectTransform Rect(string name,float w,float h,Transform? p=null){var r=new GameObject(name).transform;r.SetParent(p,false);r.sizeDelta=new(w,h);return r;}
void Bag(Transform parent){var r=Rect("bag",583,250,parent);var grids=new[]{Rect("main",190,250,r),Rect("pocket1",127,190,r),Rect("pocket2",127,190,r),Rect("pocket3",127,190,r)};float x=0;foreach(var g in grids){g.anchoredPosition=new(x,0);x+=g.rect.width+4;}ExpandedStorageNavigation.Register(r,grids);}
var canvas=Rect("Canvas",1920,1080);canvas.gameObject.AddComponent(typeof(Canvas));
var host=Rect("inventory",600,700,canvas);var scroll=(ScrollRect)host.gameObject.AddComponent(typeof(EFT.UI.ScrollRectNoDrag));scroll.viewport=Rect("view",600,700,host);scroll.content=Rect("content",600,1300,scroll.viewport);Bag(scroll.content);
for(int i=0;i<16;i++){var w=Rect("window "+i,700,900,canvas);w.gameObject.AddComponent(typeof(EFT.UI.GridWindow));Bag(w);}
for(int i=0;i<5;i++)ExpandedStorageNavigation.Maintain();
var size=RectTransform.SizeWrites;var marks=LayoutRebuilder.Marks;var norm=ScrollRect.NormalizedWrites;var lookups=GameObject.ComponentLookups;
for(int i=0;i<300;i++)ExpandedStorageNavigation.Maintain();
var result=new{idleFrames=300,standaloneWindows=16,inventoryViews=1,rectTransformSizeWrites=RectTransform.SizeWrites-size,layoutMarks=LayoutRebuilder.Marks-marks,normalizedPositionWrites=ScrollRect.NormalizedWrites-norm,ancestorComponentLookups=GameObject.ComponentLookups-lookups,boundary="Production adapter with controlled UGUI-shaped doubles; native API call counts, not a running game or FPS measurement."};
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
return result.rectTransformSizeWrites==0&&result.layoutMarks==0&&result.normalizedPositionWrites==0&&result.ancestorComponentLookups==0?0:1;
