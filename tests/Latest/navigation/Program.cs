using UnityEngine;
using UnityEngine.UI;
using EscapeFromBorderlands.Client;
using Nav=EscapeFromBorderlands.Client.ExpandedStorageNavigation;
using Layout=EscapeFromBorderlands.Client.BackpackGridLayout;
int cases=0,checks=0,failures=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
void Eq(float a,float b,string message)=>Check(Math.Abs(a-b)<.02f,$"{message}: {a}!={b}");
void Run(string name,Action test){cases++;Nav.Reset();Plugin.LogSource.Errors.Clear();try{test();Check(Plugin.LogSource.Errors.Count==0,string.Join("\n",Plugin.LogSource.Errors));Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e);}}
RectTransform Rect(string name,float width,float height,Transform? parent=null){var r=new GameObject(name).transform;r.SetParent(parent,false);r.sizeDelta=new(width,height);return r;}
(RectTransform Root,RectTransform[] Grids) Pockets(Transform parent,float[] widths,float[] heights){
 var root=Rect("legendary grid",widths.Sum()+4*(widths.Length-1),heights.Max(),parent);var grids=new RectTransform[widths.Length];float x=0;
 for(int i=0;i<widths.Length;i++){grids[i]=Rect("pocket "+i,widths[i],heights[i],root);grids[i].anchoredPosition=new(x,0);x+=widths[i]+4;}
 return(root,grids);
}
(RectTransform Canvas,ScrollRect Scroll,RectTransform Content,RectTransform Root,RectTransform[] Grids) Scene(float viewport=600){
 var canvas=Rect("Canvas",1920,1080);canvas.gameObject.AddComponent(typeof(Canvas));var host=Rect("inventory",viewport,700,canvas);var s=(ScrollRect)host.gameObject.AddComponent(typeof(EFT.UI.ScrollRectNoDrag));
 s.viewport=Rect("viewport",viewport,700,host);var content=Rect("content",viewport,1300,s.viewport);s.content=content;s.horizontal=false;s.vertical=true;
 var(root,grids)=Pockets(content,[190,127,127,127],[190,190,190,190]);return(canvas,s,content,root,grids);
}
GameObject? Named(RectTransform canvas,string name)=>canvas.gameObject.Walk().FirstOrDefault(g=>g.name==name&&!g.Destroyed);
void Tick(){Nav.Maintain();Check(Plugin.LogSource.Errors.Count==0,string.Join("\n",Plugin.LogSource.Errors));}
void Fit(RectTransform root,RectTransform[] grids,float budget){
 Check(root.rect.width<=budget+.02f,"whole layout inside hard width");
 for(int i=0;i<grids.Length;i++){
  var a=grids[i];float w=a.rect.width*a.localScale.x,h=a.rect.height*a.localScale.y;var p=a.anchoredPosition;
  Check(p.x>=0&&p.x+w<=budget+.02f,"pocket inside width");Check(-p.y+h<=root.rect.height+.02f,"root contains full pocket height");
  for(int j=i+1;j<grids.Length;j++){var b=grids[j];var q=b.anchoredPosition;var bw=b.rect.width*b.localScale.x;var bh=b.rect.height*b.localScale.y;
   Check(p.x+w<=q.x+.02f||q.x+bw<=p.x+.02f||-p.y+h<=-q.y+.02f||-q.y+bh<=-p.y+.02f,"whole pockets do not overlap");}
 }
}
Run("Flat Pack plus 1 column follows drawing with 3 pockets above and 1 below",()=>{
 var(c,s,content,root,grids)=Scene();Nav.Register(root,grids);Tick();
 Eq(grids[0].anchoredPosition.x,0,"first pocket left");Eq(grids[1].anchoredPosition.x,194,"second pocket right");Eq(grids[2].anchoredPosition.x,325,"third pocket right");
 Eq(grids[3].anchoredPosition.x,0,"fourth returns left");Eq(grids[3].anchoredPosition.y,-194,"fourth moves below complete first row");Eq(root.rect.height,384,"complete second row reserved");
 Fit(root,grids,457);Check(!s.horizontal&&s.horizontalScrollbar==null,"no sideways navigation");Check(Named(c,"EFB Reset view")==null&&Named(c,"EFB horizontal scrollbar")==null,"old controls absent");
});
Run("All Flat Pack row and column rolls preserve exact pocket sizes",()=>{
 foreach(var row in new[]{0,1})foreach(var col in new[]{0,1}){
  var(c,s,content,root,_)=Scene();var(r,g)=Pockets(content,[127+63*col,127,127,127],[190+63*row,190,190,190]);
  Nav.Register(r,g);Tick();Eq(g[0].rect.width,127+63*col,"true rolled columns");Eq(g[0].rect.height,190+63*row,"true rolled rows");Fit(r,g,457);
  foreach(var grid in g)Eq(grid.localScale.x,1,"ordinary pockets stay full size");
 }
});
Run("Row wrapping uses the tallest pocket before starting the next row",()=>{
 var old=new[]{new Layout.Placement(0,0),new(194,0),new(325,0),new(456,0)};
 var plan=Layout.Wrap(old,[190,127,127,127],[253,190,190,190],457,4);Eq(plan.Positions[3].Y,257,"clear tallest first-row pocket");Eq(plan.Height,447,"full wrapped height");
});
Run("Visual order stays stable when native grid indices are reversed",()=>{
 var old=new[]{new Layout.Placement(456,0),new(325,0),new(194,0),new(0,0)};
 var plan=Layout.Wrap(old,[127,127,127,190],[190,190,190,190],457,4);
 Eq(plan.Positions[3].X,0,"visual first grid stays first");Eq(plan.Positions[0].Y,194,"visual last grid wraps despite index 0");
});
Run("Pockets that already fit retain their original geometry",()=>{
 var old=new[]{new Layout.Placement(20,10),new(277,10),new(20,330)};
 var plan=Layout.Wrap(old,[253,127,127],[316,127,127],457,4);
 for(int i=0;i<old.Length;i++){Eq(plan.Positions[i].X,old[i].X,"original x");Eq(plan.Positions[i].Y,old[i].Y,"original y");Eq(plan.Scales[i],1,"native display scale");}
});
Run("Horizontal limit follows narrow viewports and returns to original packing on resize",()=>{
 var(c,s,content,root,grids)=Scene(400);Nav.Register(root,grids);Tick();Fit(root,grids,396);Eq(grids[2].anchoredPosition.y,-194,"third wraps in narrower panel");
 s.viewport.sizeDelta=new(700,700);Tick();Eq(grids[2].anchoredPosition.y,0,"third fits again");Fit(root,grids,457);Eq(content.rect.width,700,"native panel width follows viewport");
 s.viewport.sizeDelta=new(300,700);Tick();Fit(root,grids,296);Eq(content.rect.height,1300,"native vertical content height untouched by width clamp");
});
Run("Left-side equipment offset reduces the usable pocket width",()=>{
 var(c,s,content,root,grids)=Scene(520);root.anchoredPosition=new(100,-200);Nav.Register(root,grids);Tick();Fit(root,grids,416);Eq(grids[2].anchoredPosition.y,-194,"thumbnail/left margin accounted for");Check(root.anchoredPosition.x==100,"native row placement retained");
});
Run("Layout under scaled ancestors measures the limit in correct local units",()=>{
 var(c,s,content,root,grids)=Scene(500);root.localScale=new(1.5f,1.5f,1);Nav.Register(root,grids);Tick();Fit(root,grids,496/1.5f);Check(root.rect.width*1.5f<=496.02f,"visible width remains bounded");
});
Run("Wider displays never exceed the fixed maximum",()=>{
 var(c,s,content,root,grids)=Scene(1400);Nav.Register(root,grids);Tick();Fit(root,grids,457);Eq(grids[3].anchoredPosition.y,-194,"hard limit still wraps fourth");
});
Run("Extra-wide single pocket fits its display without changing actual cells",()=>{
 var(c,s,content,root,_)=Scene(400);var(r,g)=Pockets(content,[900],[1000]);Nav.Register(r,g);Tick();Fit(r,g,396);
 Eq(g[0].rect.width,900,"logical width untouched");Eq(g[0].rect.height,1000,"logical height untouched");Eq(g[0].localScale.x,396f/900,"display fit factor");Eq(g[0].localScale.y,g[0].localScale.x,"uniform aspect ratio");
 // Native GridView.CalculateItemLocation uses InverseTransformPoint before /63.
 var local=new Vector3(2*63,3*63);var world=g[0].TransformPoint(local);var roundtrip=g[0].InverseTransformPoint(world);Eq(roundtrip.x/63,2,"same native column hit");Eq(roundtrip.y/63,3,"same native row hit");
});
Run("Display fitting restores correctly on wider viewports without accumulating scale",()=>{
 var(c,s,content,root,_)=Scene(250);var(r,g)=Pockets(content,[700],[800]);Nav.Register(r,g);Tick();var small=g[0].localScale.x;
 s.viewport.sizeDelta=new(900,700);for(int i=0;i<100;i++)Tick();Eq(g[0].localScale.x,457f/700,"fit comes from original size every time");Check(g[0].localScale.x>small,"wider panel increases displayed size");
});
Run("Item dragging freezes pocket locations until the drag ends",()=>{
 var(c,s,content,root,grids)=Scene(600);bool dragging=false;Nav.Register(root,grids,()=>dragging);Tick();var positions=grids.Select(g=>g.anchoredPosition).ToArray();
 dragging=true;s.viewport.sizeDelta=new(300,700);Tick();for(int i=0;i<grids.Length;i++){Eq(grids[i].anchoredPosition.x,positions[i].x,"drag x stable");Eq(grids[i].anchoredPosition.y,positions[i].y,"drag y stable");}
 dragging=false;Tick();Fit(root,grids,296);Check(grids[1].anchoredPosition.y<0,"pending width applied after drag");
});
Run("Vertical position and inertia survive wrapping",()=>{
 var(c,s,content,root,grids)=Scene();s.verticalNormalizedPosition=.37f;s.velocity=new(250,-80);content.anchoredPosition=new(0,-220);Nav.Register(root,grids);Tick();
 Eq(s.verticalNormalizedPosition,.37f,"vertical scroll kept");Eq(content.anchoredPosition.y,-220,"vertical content position kept");Eq(s.velocity.x,0,"sideways inertia stopped");Eq(s.velocity.y,-80,"vertical inertia kept");Check(s.vertical,"vertical scrolling retained");
});
var harmony=new HarmonyLib.Harmony("efb.validation.wrap.autopan");harmony.CreateClassProcessor(typeof(ExpandedStorageAutoPanPatch)).Patch();
Run("Actual Harmony drag helper blocks sideways panning and retains native vertical movement",()=>{
 var(c,s,content,root,grids)=Scene();Nav.Register(root,grids);Tick();content.anchoredPosition=new(0,-20);
 EFT.UI.UIExtensions.KeepChildInScrollViewPort(s,grids[0]);Eq(content.anchoredPosition.x,0,"native horizontal auto-pan removed");Eq(content.anchoredPosition.y,-80,"native vertical auto-pan kept");Eq(s.velocity.x,0,"horizontal drag inertia stopped");Eq(s.velocity.y,-10,"native vertical inertia kept");
});
Run("Native drag panning outside EFB-managed panels remains unchanged",()=>{
 var(c,s,content,root,grids)=Scene();EFT.UI.UIExtensions.KeepChildInScrollViewPort(s,grids[0]);Eq(content.anchoredPosition.x,-170,"unregistered native x unchanged");Eq(content.anchoredPosition.y,-60,"unregistered native y unchanged");
});
Run("Multiple expanded containers share their native panel without adding controls",()=>{
 var(c,s,content,root,grids)=Scene();var(r,g)=Pockets(content,[400,200],[400,400]);Nav.Register(root,grids);Nav.Register(r,g);Tick();Fit(root,grids,457);Fit(r,g,457);
 root.gameObject.SetActive(false);Tick();Check(!s.horizontal,"remaining bag still managed");r.gameObject.SetActive(false);Tick();Check(!s.horizontal&&s.horizontalScrollbar==null,"original native state restored");
});
Run("Existing horizontal bar is hidden while managed and restored after close",()=>{
 var(c,s,content,root,grids)=Scene();var bar=(Scrollbar)new GameObject("native horizontal").AddComponent(typeof(Scrollbar));s.horizontal=true;s.horizontalScrollbar=bar;
 Nav.Register(root,grids);Tick();Check(s.horizontalScrollbar==null&&!bar.gameObject.activeSelf,"no horizontal bar exposed");root.gameObject.SetActive(false);Tick();Check(s.horizontal&&ReferenceEquals(s.horizontalScrollbar,bar)&&bar.gameObject.activeSelf,"original bar and state restored");
});
Run("Repeated opening and destruction retain grids and leave no stale panel changes",()=>{
 var(c,s,content,root,grids)=Scene();Nav.Register(root,grids);for(int i=0;i<20;i++){root.gameObject.SetActive(false);Tick();root.gameObject.SetActive(true);Tick();Fit(root,grids,457);}
 Check(grids.All(g=>!g.Destroyed),"native pockets retained");UnityEngine.Object.Destroy(root.gameObject);Tick();Check(!s.horizontal&&s.horizontalScrollbar==null,"destroy cleanup");
});
Run("Standalone windows use only vertical scrolling with bounded wrapped width",()=>{
 var canvas=Rect("Canvas",1000,800);canvas.gameObject.AddComponent(typeof(Canvas));var window=Rect("bag window",900,1000,canvas);window.gameObject.AddComponent(typeof(EFT.UI.GridWindow));
 var(root,g)=Pockets(window,[300,300,300,300],[700,700,700,700]);Nav.Register(root,g);Tick();var wrapper=Named(canvas,"EFB container viewport")!.transform;var s=(ScrollRect)wrapper.gameObject.GetComponent(typeof(ScrollRect))!;
 Fit(root,g,457);Check(wrapper.rect.width<=479&&wrapper.rect.height<=600,"window bounds respect hard cap and canvas");Check(!s.horizontal&&s.horizontalScrollbar==null&&s.vertical&&s.verticalScrollbar!=null,"vertical-only controls");
 Check(s.viewport.gameObject.GetComponent(typeof(RectMask2D))!=null,"native UI clipping viewport");Check(ReferenceEquals(s.content,root),"same native grid views");Eq(g[0].rect.width,300,"true pocket width");
 Nav.Reset();Check(ReferenceEquals(root.parent,window)&&g.All(grid=>!grid.Destroyed),"safe native unwrap");
});
Run("Standalone canvas resize reflows without stale wrappers",()=>{
 var canvas=Rect("Canvas",1920,1080);canvas.gameObject.AddComponent(typeof(Canvas));var window=Rect("window",1000,1200,canvas);window.gameObject.AddComponent(typeof(EFT.UI.GridWindow));var(root,g)=Pockets(window,[250,250,250],[400,400,400]);
 Nav.Register(root,g);Tick();var wrapper=Named(canvas,"EFB container viewport")!.transform;canvas.sizeDelta=new(400,600);Tick();Fit(root,g,318);Check(wrapper.rect.width<=340,"canvas limit updates");
 UnityEngine.Object.Destroy(root.gameObject);Tick();Check(wrapper==null,"disposed private wrapper cleaned");
});
Run("Rebinding a fitted view starts from native scales rather than multiplying the old fit",()=>{
 var(c,s,content,root,_)=Scene(250);var(r,g)=Pockets(content,[700],[800]);Nav.Register(r,g);Tick();Eq(g[0].localScale.x,246f/700,"initial fit");
 // GeneratedGridsView.Show restores native logical geometry before registering.
 r.sizeDelta=new(700,800);g[0].anchoredPosition=new(0,0);Nav.Register(r,g);Tick();Eq(g[0].localScale.x,246f/700,"fresh binding uses original scale");
});
Run("Wrapping stays within bounds without overlaps across varied compartment shapes",()=>{
 var random=new Random(270);for(int pass=0;pass<300;pass++){
  int n=random.Next(1,15);var widths=new float[n];var heights=new float[n];var positions=new Layout.Placement[n];float x=0;
  for(int i=0;i<n;i++){widths[i]=random.Next(1,14)*63+1;heights[i]=random.Next(1,20)*63+1;positions[i]=new(x,0);x+=widths[i]+4;}
  float limit=random.Next(128,900);var plan=Layout.Wrap(positions,widths,heights,limit,4);float cap=Math.Min(457,limit);Check(plan.Width<=cap+.02f,"random layout bounded");
  for(int i=0;i<n;i++){var a=plan.Positions[i];float w=widths[i]*plan.Scales[i],h=heights[i]*plan.Scales[i];Check(a.X>=0&&a.X+w<=cap+.02f&&a.Y+h<=plan.Height+.02f,"random pocket retained inside root");Check(plan.Scales[i]>0&&plan.Scales[i]<=1,"display never grows cells");
   for(int j=i+1;j<n;j++){var b=plan.Positions[j];Check(a.X+w<=b.X+.02f||b.X+widths[j]*plan.Scales[j]<=a.X+.02f||a.Y+h<=b.Y+.02f||b.Y+heights[j]*plan.Scales[j]<=a.Y+.02f,"random pockets separated");}
  }
 }
});

Run("Long Gun Logistics preserves the body stack and places its gun sleeve alongside when width permits",()=>{
 var original=new[]{new Layout.Placement(0,0),new(194,0),new(0,320),new(325,0)};
 var plan=Layout.Wrap(original,[190,127,190,190],[316,442,127,631],400,4,3,true);
 Eq(plan.Positions[0].X,0,"main at left");Eq(plan.Positions[2].X,0,"lower native pocket stays with main");Eq(plan.Positions[2].Y,320,"native stacked spacing retained");
 Eq(plan.Positions[3].X,194,"restricted sleeve at right");Eq(plan.Positions[3].Y,0,"sleeve starts at top");Eq(plan.Positions[1].Y,451,"small side pocket goes below complete body stack");
 Eq(plan.Height,893,"compact grouped height");Check(plan.Width<=400,"hard cap kept");
});
Run("Long Gun Logistics narrow views move whole native stacks without separating their pockets",()=>{
 var old=new[]{new Layout.Placement(0,0),new(194,0),new(0,320),new(325,0)};
 foreach(var limit in new[]{250f,320f,400f,457f}){
  var plan=Layout.Wrap(old,[190,127,190,190],[316,442,127,631],limit,4,3,true);
  Eq(plan.Positions[2].X,plan.Positions[0].X,"stack x retained");Eq(plan.Positions[2].Y-plan.Positions[0].Y,320,"stack y retained");
  Check(plan.Width<=limit+.01f,"whole group respects cap");
  for(int i=0;i<4;i++)for(int j=i+1;j<4;j++){var a=plan.Positions[i];var b=plan.Positions[j];var w=new[]{190f,127,190,190};var h=new[]{316f,442,127,631};Check(a.X+w[i]<=b.X+.02||b.X+w[j]<=a.X+.02||a.Y+h[i]<=b.Y+.02||b.Y+h[j]<=a.Y+.02,"no grouped overlaps");}
 }
});
Run("Native stacked compartments remain together when a normal backpack must wrap",()=>{
 var old=new[]{new Layout.Placement(0,0),new(0,320),new(260,0)};
 var plan=Layout.Wrap(old,[253,127,253],[316,127,442],400,4,3,false);
 Eq(plan.Positions[1].X,plan.Positions[0].X,"lower pocket same column");Eq(plan.Positions[1].Y-plan.Positions[0].Y,320,"lower pocket stays below main");Eq(plan.Positions[2].Y,451,"next column clears entire native stack");
});
Run("Production UI registration forwards native stack and gun sleeve metadata",()=>{
 var(c,s,content,_,_g)=Scene(404);var root=Rect("Long Gun Logistics",515,631,content);
 var grids=new[]{Rect("main",190,316,root),Rect("side",127,442,root),Rect("bottom",190,127,root),Rect("sleeve",190,631,root)};
 grids[1].anchoredPosition=new(194,0);grids[2].anchoredPosition=new(0,-320);grids[3].anchoredPosition=new(325,0);
 Nav.Register(root,grids,null,3,true);Tick();Eq(grids[2].anchoredPosition.y,-320,"stack retained in actual adapter");Eq(grids[3].anchoredPosition.x,194,"actual sleeve alongside");Fit(root,grids,400);
});
Run("Idle backpack views do not repeatedly rebuild layouts or search component hierarchies",()=>{
 var(c,s,content,root,g)=Scene();Nav.Register(root,g);for(int i=0;i<5;i++)Tick();
 int sizes=RectTransform.SizeWrites,marks=LayoutRebuilder.Marks,normalized=ScrollRect.NormalizedWrites,lookups=GameObject.ComponentLookups;
 for(int i=0;i<300;i++)Tick();
 Check(RectTransform.SizeWrites==sizes,"0 idle RectTransform size writes");Check(LayoutRebuilder.Marks==marks,"0 idle layout rebuild requests");Check(ScrollRect.NormalizedWrites==normalized,"0 idle normalized-position writes");Check(GameObject.ComponentLookups==lookups,"0 idle ancestor component searches");
});
Run("Many simultaneously open backpack windows remain idle without layout churn",()=>{
 var canvas=Rect("Canvas",1920,1080);canvas.gameObject.AddComponent(typeof(Canvas));
 for(int i=0;i<16;i++){var window=Rect("window "+i,700,900,canvas);window.gameObject.AddComponent(typeof(EFT.UI.GridWindow));var(r,g)=Pockets(window,[190,127,127,127],[316,190,190,190]);Nav.Register(r,g);}
 for(int i=0;i<5;i++)Tick();
 int sizes=RectTransform.SizeWrites,marks=LayoutRebuilder.Marks,normalized=ScrollRect.NormalizedWrites,lookups=GameObject.ComponentLookups;
 for(int i=0;i<300;i++)Tick();
 Check(RectTransform.SizeWrites==sizes&&LayoutRebuilder.Marks==marks,"16 open windows make 0 idle sizing/rebuild calls");Check(ScrollRect.NormalizedWrites==normalized,"16 open windows make 0 idle scroll resets");Check(GameObject.ComponentLookups==lookups,"16 open windows make 0 idle ancestor searches");
});

Run("7-column cap allows native pocket gaps but never an 8-column row",()=>{
 Check(Layout.MaximumWidth==457,"7 columns plus usual pocket gaps");Check(Layout.MaximumWidth<8*63+1,"8 cells cannot fit at native size");
 var seven=Layout.Wrap([new(0,0)],[442],[300],1000,4);Eq(seven.Scales[0],1,"7-wide compartment stays full size");
 var eight=Layout.Wrap([new(0,0)],[505],[300],1000,4);Check(eight.Scales[0]<1&&eight.Width<=457,"saved 8-wide fits by display scaling");
});
harmony.UnpatchSelf();Nav.Reset();Console.WriteLine($"Expanded storage wrapping: {cases-failures}/{cases} cases passed; {checks} assertions; {failures} failures.");return failures==0?0:1;
