using System;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    // Full-screen felt and chip artwork remain sharp at any window size.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PokerTableArt : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r=rectTransform.rect;
            var edge=new Color(.065f,.235f,.175f);
            vh.AddVert(r.center,new Color(.12f,.33f,.255f),Vector2.zero);
            vh.AddVert(new Vector2(r.xMin,r.yMin),edge,Vector2.zero);
            vh.AddVert(new Vector2(r.xMin,r.yMax),edge,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax,r.yMax),edge,Vector2.zero);
            vh.AddVert(new Vector2(r.xMax,r.yMin),edge,Vector2.zero);
            vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);
            vh.AddTriangle(0,3,4);vh.AddTriangle(0,4,1);
        }

        internal static void Ellipse(VertexHelper vh, Vector2 c, float rx, float ry, Color centre, Color edge)
        {
            const int segments=96;
            var start=vh.currentVertCount;
            vh.AddVert(c,centre,Vector2.zero);
            for(var i=0;i<=segments;i++)
            {
                var a=i*Mathf.PI*2/segments;
                vh.AddVert(c+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry),edge,Vector2.zero);
                if(i>0) vh.AddTriangle(start,start+i,start+i+1);
            }
        }

        internal static void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color color)
        {
            var n=vh.currentVertCount;
            vh.AddVert(a,color,Vector2.zero); vh.AddVert(b,color,Vector2.zero);
            vh.AddVert(c,color,Vector2.zero); vh.AddVert(d,color,Vector2.zero);
            vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PokerChipGraphic : MaskableGraphic
    {
        private int _count;
        private Color _chip;
        public void Set(int count,Color chip) { _count=count;_chip=chip;SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var layers=Mathf.Min(8,_count);
            for(var j=0;j<layers;j++)
            {
                var y=j*3.3f;
                PokerTableArt.Ellipse(vh,new Vector2(1,y-3),17,8,Color.black,new Color(0,0,0,.3f));
                PokerTableArt.Ellipse(vh,new Vector2(0,y-1.5f),17,8,_chip*.6f,_chip*.55f);
                PokerTableArt.Ellipse(vh,new Vector2(0,y),17,8,_chip,_chip);
                for(var stripe=0;stripe<10;stripe++)
                {
                    var a=stripe*Mathf.PI/5;var b=a+.24f;
                    PokerTableArt.Quad(vh,Point(a,17,8,y),Point(b,17,8,y),
                        Point(b,12.4f,5.8f,y),Point(a,12.4f,5.8f,y),new Color(.92f,.88f,.72f));
                }
                if(j==layers-1)
                {
                    PokerTableArt.Ellipse(vh,new Vector2(0,y),11.5f,5.5f,new Color(.87f,.8f,.61f),new Color(.87f,.8f,.61f));
                    PokerTableArt.Ellipse(vh,new Vector2(0,y),10.7f,4.9f,_chip,_chip);
                }
            }
        }
        private static Vector2 Point(float angle,float x,float y,float cy)=>new Vector2(Mathf.Cos(angle)*x,cy+Mathf.Sin(angle)*y);
    }

    public sealed class PokerChipPile
    {
        public static readonly int[] Denominations={1,5,25,100,500};
        public static readonly Color[] Colors={new Color(.89f,.86f,.76f),new Color(.72f,.16f,.17f),
            new Color(.12f,.48f,.34f),new Color(.11f,.13f,.16f),new Color(.44f,.27f,.64f)};
        public RectTransform Root {get;private set;}
        private PokerChipGraphic[] _chips;
        private Text[] _values,_counts;
        private int _amount=-1;

        public static PokerChipPile Create(Transform parent,Font font,Vector2 position,float scale,bool counts)
        {
            var pile=new PokerChipPile();
            pile.Root=PokerUi.Rect(parent,"Chip stacks",position,new Vector2(200,70));
            pile.Root.localScale=Vector3.one*scale;
            pile._chips=new PokerChipGraphic[5];pile._values=new Text[5];pile._counts=new Text[5];
            for(var i=0;i<5;i++)
            {
                var r=PokerUi.Rect(pile.Root,"Chip "+Denominations[i],new Vector2((i-2)*38,0),new Vector2(36,60));
                pile._chips[i]=r.gameObject.AddComponent<PokerChipGraphic>();pile._chips[i].raycastTarget=false;
                pile._values[i]=PokerUi.Label(r,font,"",11,Vector2.zero,new Vector2(33,18),i==0?PokerUi.Dark:PokerUi.Paper);
                pile._counts[i]=PokerUi.Label(r,font,"",12,new Vector2(0,-17),new Vector2(38,17),PokerUi.Muted);
                pile._counts[i].gameObject.SetActive(counts);
            }
            return pile;
        }

        // Keep useful change on the table. For2000 this is20x1,16x5,12x25,6x100,2x500.
        public static int[] Breakdown(int amount)
        {
            var result=new int[5];var desired=new[]{20,16,12,6};
            for(var i=0;i<4;i++)
            {
                var available=amount/Denominations[i];
                var ratio=Denominations[i+1]/Denominations[i];
                result[i]=available<=desired[i]?available:desired[i]+(available-desired[i])%ratio;
                amount-=result[i]*Denominations[i];
            }
            result[4]=amount/500;return result;
        }

        public void SetAmount(int amount)
        {
            amount=Math.Max(0,amount);
            if(_amount==amount)return;
            _amount=amount;Root.gameObject.SetActive(amount>0);
            var counts=Breakdown(amount);
            var visible=0;foreach(var count in counts)if(count>0)visible++;
            var index=0;
            for(var i=0;i<5;i++)
            {
                var shown=counts[i]>0;
                _chips[i].gameObject.SetActive(shown);
                if(!shown)continue;
                _chips[i].rectTransform.anchoredPosition=new Vector2((index++-(visible-1)*.5f)*38,0);
                _chips[i].Set(counts[i],Colors[i]);
                _values[i].text=Denominations[i].ToString();
                _values[i].rectTransform.anchoredPosition=new Vector2(0,(Mathf.Min(8,counts[i])-1)*3.3f);
                _counts[i].text="×"+counts[i];
            }
        }
    }

    internal static class PokerUi
    {
        internal static readonly Color Gold=new Color(.89f,.72f,.38f),Paper=new Color(.96f,.93f,.84f),
            Dark=new Color(.035f,.085f,.07f),Muted=new Color(.62f,.72f,.65f),Red=new Color(.6f,.18f,.16f);
        internal static RectTransform Rect(Transform parent,string name,Vector2 pos,Vector2 size)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);
            r.anchoredPosition=pos;r.sizeDelta=size;return r;
        }
        internal static void Stretch(RectTransform r)
        {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
        internal static Image Box(Transform parent,string name,Vector2 pos,Vector2 size,Color color,float radius=12)
        {
            var r=Rect(parent,name,pos,size);var im=r.gameObject.AddComponent<Image>();
            im.sprite=UiTextures.RoundedRect(64,64,radius,Color.white);im.type=Image.Type.Sliced;
            im.color=color;im.raycastTarget=false;return im;
        }
        internal static Text Label(Transform parent,Font font,string text,int size,Vector2 pos,Vector2 dimensions,Color color)
        {
            var t=Rect(parent,text.Length>0?text:"Label",pos,dimensions).gameObject.AddComponent<Text>();
            t.font=font;t.text=text;t.fontSize=MobileUi.TextSize(size);t.color=color;t.alignment=TextAnchor.MiddleCenter;
            t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Wrap;return t;
        }
        internal static Button Button(Transform parent,Font font,string text,Vector2 pos,Vector2 size,Color color,Action action)
        {
            var image=Box(parent,text,pos,size,color);image.raycastTarget=true;
            var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;
            var colors=b.colors;colors.highlightedColor=new Color(1.12f,1.12f,1.12f);colors.pressedColor=new Color(.8f,.8f,.8f);
            colors.disabledColor=new Color(.36f,.42f,.38f,.75f);b.colors=colors;
            b.onClick.AddListener(()=>action());
            Label(image.transform,font,text,19,Vector2.zero,size-new Vector2(12,4),Paper);
            return b;
        }
        internal static string Money(int n)=>n.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
