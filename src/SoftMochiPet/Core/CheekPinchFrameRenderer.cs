using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace SoftMochiPet.Core;

/// <summary>Continuous facial rig on a stable cel; no whole-body frame crossfades.</summary>
public sealed class CheekPinchFrameRenderer
{
    private readonly DrawingVisual _visual = new();
    private BitmapSource? _source;
    private RenderTargetBitmap? _output;
    private SolidColorBrush? _skin;
    private System.Windows.Media.Brush? _cheekSkin;
    private readonly BitmapSource?[] _eyes = new BitmapSource?[2];
    private readonly Rect[] _eyeBounds = new Rect[2];
    private readonly Geometry?[] _eyeMasks = new Geometry?[2];
    private System.Windows.Media.Brush? _eyeSkin;
    private double _lastX = double.NaN, _lastY, _lastNibble;
    private CheekSide _lastSide;
    private static readonly SolidColorBrush Mouth = Brush(Color.FromRgb(162,70,93));
    private static readonly SolidColorBrush Tongue = Brush(Color.FromRgb(239,145,158));
    private static readonly SolidColorBrush Ink = Brush(Color.FromRgb(44,36,43));
    private static readonly SolidColorBrush Tooth = Brush(Color.FromRgb(255,250,237));
    private static readonly RadialGradientBrush Blush = MakeBlush();

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush MakeBlush()
    {
        var brush = new RadialGradientBrush(Color.FromArgb(138,241,135,148),Color.FromArgb(0,241,135,148));
        brush.Freeze();
        return brush;
    }

    public void Clear()
    {
        _source = null;
        _output = null;
        _skin = null;
        _cheekSkin = null;
        _eyes[0] = _eyes[1] = null;
        _lastX = double.NaN;
    }

    private void Prepare(BitmapSource source, CheekPinchGeometry g)
    {
        var converted = new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect((int)g.MouthX-12,(int)g.MouthY,1,1),pixel,4,0);
        var skin = Color.FromRgb(pixel[2],pixel[1],pixel[0]);
        _skin = Brush(skin);
        var shade = Color.FromRgb((byte)Math.Max(0,skin.R-2),(byte)Math.Max(0,skin.G-10),(byte)Math.Max(0,skin.B-9));
        var gradient = new LinearGradientBrush(new GradientStopCollection {
            new(skin,0),new(skin,.60),new(shade,1) },new Point(0,0),new Point(0,1));
        gradient.Freeze();
        _cheekSkin = gradient;
        var feibi = g.MouthX>185;
        Color Sample(int x,int y)
        {
            converted.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);
            return Color.FromRgb(pixel[2],pixel[1],pixel[0]);
        }
        var eyeSkin=new LinearGradientBrush(new GradientStopCollection {
            new(Sample(feibi?189:176,feibi?207:205),0),
            new(Sample(feibi?189:176,feibi?231:215),.7),
            new(Sample(feibi?189:176,feibi?250:242),1) },new Point(0,feibi?197:191),new Point(0,feibi?252:246))
            { MappingMode=BrushMappingMode.Absolute };
        eyeSkin.Freeze();
        _eyeSkin=eyeSkin;
        _eyeBounds[0] = feibi ? new Rect(126,198,54,48) : new Rect(121,191,29,28);
        _eyeBounds[1] = feibi ? new Rect(204,198,46,48) : new Rect(197,191,31,28);
        for (var i=0;i<2;i++)
        {
            var rect=_eyeBounds[i];
            _eyeMasks[i]=feibi ? Geometry.Parse(i==0
                ? "M 124,215 C 129,199 149,193 165,200 C 182,206 184,233 169,243 C 149,253 121,246 121,230 Z"
                : "M 204,213 C 204,199 219,193 235,200 C 251,206 255,226 245,240 C 234,253 206,248 204,231 Z")
                : new RectangleGeometry(rect,5,7);
            _eyeMasks[i]!.Freeze();
            var crop=new CroppedBitmap(source,new Int32Rect((int)rect.X,(int)rect.Y,(int)rect.Width,(int)rect.Height));
            crop.Freeze();
            _eyes[i]=crop;
        }
        _source=source;
        _output=new RenderTargetBitmap(source.PixelWidth,source.PixelHeight,96,96,PixelFormats.Pbgra32);
        _lastX=double.NaN;
    }

    public BitmapSource Render(BitmapSource source,CheekPinchGeometry g,CheekSide side,
        double pullX,double pullY,double nibble)
    {
        pullX=double.IsFinite(pullX)?Math.Clamp(pullX,-105,105):0;
        pullY=double.IsFinite(pullY)?Math.Clamp(pullY,-42,42):0;
        nibble=double.IsFinite(nibble)?Math.Clamp(nibble,0,1):0;
        if(Math.Abs(pullX)<.01 && Math.Abs(pullY)<.01 && nibble<.001) return source;
        if(!ReferenceEquals(_source,source)) Prepare(source,g);
        if(_lastX==pullX && _lastY==pullY && _lastNibble==nibble && _lastSide==side) return _output!;
        var direction=side==CheekSide.Left?-1:1;
        var outward=Math.Clamp(direction*pullX/105,0,1);
        var vertical=pullY/42;
        var tension=Math.Clamp(Math.Sqrt(outward*outward+vertical*vertical*.45),0,1);
        using(var dc=_visual.RenderOpen())
        {
            dc.DrawImage(source,new Rect(0,0,source.PixelWidth,source.PixelHeight));
            if(tension>.001)
            {
                DrawCheek(dc,g,side,outward*64,vertical*26,tension);
                DrawExpression(dc,g,direction,tension,nibble);
            }
        }
        _output!.Clear();
        _output.Render(_visual);
        (_lastX,_lastY,_lastNibble,_lastSide)=(pullX,pullY,nibble,side);
        return _output;
    }

    private void DrawCheek(DrawingContext dc,CheekPinchGeometry g,CheekSide side,
        double pull,double lift,double tension)
    {
        var feibi=g.MouthX>185;
        var left=side==CheekSide.Left;
        var d=left?-1:1;
        Point start,bend,end;
        if(feibi)
            (start,bend,end)=left?(new Point(124,221),new Point(122,242),new Point(162,254))
                                  :(new Point(253,221),new Point(254,242),new Point(220,254));
        else
            (start,bend,end)=left?(new Point(113,216),new Point(108,240),new Point(147,248))
                                  :(new Point(248,216),new Point(249,241),new Point(222,246));
        var edge=new StreamGeometry();
        var patch=new StreamGeometry();
        // A rounded grip and broad fixed jaw root keep the stretch attached.
        var tip=new Point(bend.X+d*pull,bend.Y+lift);
        var top=new Point(tip.X,tip.Y-4);
        var bottom=new Point(tip.X,tip.Y+4);
        void Contour(StreamGeometryContext path,bool filled)
        {
            path.BeginFigure(start,filled,filled);
            path.BezierTo(new(start.X+d*2,start.Y+10),new(tip.X-d*10,top.Y-2),top,true,false);
            path.BezierTo(new(tip.X+d*5,tip.Y-3),new(tip.X+d*5,tip.Y+3),bottom,true,false);
            path.BezierTo(new(tip.X-d*14,bottom.Y+5),new(end.X+d*18,end.Y+2),end,true,false);
            if(filled) path.BezierTo(new(end.X-d*3,end.Y-6),new(start.X-d*(left?7:11),start.Y+7),start,false,false);
        }
        using(var path=edge.Open()) Contour(path,false);
        using(var path=patch.Open()) Contour(path,true);
        dc.PushOpacity(Math.Clamp(tension*6,0,1));
        dc.DrawGeometry(_cheekSkin,null,patch);
        var pen=new Pen(Ink,feibi?2.1:1.15) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
        dc.DrawGeometry(null,pen,edge);
        dc.PushClip(patch);
        dc.PushTransform(new RotateTransform(Math.Atan2(lift,Math.Max(1,pull))*180/Math.PI*d,tip.X,tip.Y));
        dc.DrawEllipse(Blush,null,new Point(tip.X-d*(10+pull*.10),tip.Y),10+pull*.09,6);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private void DrawExpression(DrawingContext dc,CheekPinchGeometry g,int direction,double tension,double nibble)
    {
        var feibi=g.MouthX>185;
        var expression=Ease(Math.Clamp((tension-.08)/.62,0,1));
        for(var i=0;i<2 && expression>.001;i++)
        {
            var r=_eyeBounds[i];
            var center=new Point(r.X+r.Width/2,r.Y+r.Height/2);
            dc.PushClip(_eyeMasks[i]!);
            dc.DrawRectangle(_eyeSkin,null,new Rect(r.X-5,r.Y-3,r.Width+10,r.Height+6));
            var open=1-expression;
            if(open>.01) dc.DrawImage(_eyes[i],new Rect(r.X,center.Y-r.Height*open/2,r.Width,r.Height*open));
            dc.Pop();
            var line=new StreamGeometry();
            var sign=i==0?1:-1;
            var width=feibi?13:10;
            using(var path=line.Open())
            {
                path.BeginFigure(new(center.X-sign*width,center.Y-6*expression),false,false);
                path.LineTo(new(center.X+sign*width*.65,center.Y+1),true,false);
                path.LineTo(new(center.X-sign*width,center.Y+7*expression),true,false);
            }
            dc.PushOpacity(expression);
            dc.DrawGeometry(null,new Pen(Ink,feibi?3.6:1.8) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},line);
            dc.Pop();
        }
        dc.PushOpacity(Math.Clamp(tension*8,0,1));
        var original=new Point(g.MouthX,g.MouthY);
        dc.DrawEllipse(_skin,null,original,8,6);
        var mouth=new Point(original.X+direction*5*tension,original.Y+1);
        var height=2.5+2.4*tension+3.5*nibble;
        dc.DrawEllipse(Mouth,null,mouth,4.5+2*tension,height);
        dc.DrawEllipse(Tongue,null,new Point(mouth.X,mouth.Y+height*.38),3,Math.Max(1,height*.35));
        if(nibble>.15) dc.DrawRoundedRectangle(Tooth,null,new Rect(mouth.X-3,mouth.Y-height+1,6,2),.7,.7);
        dc.Pop();
    }

    private static double Ease(double t)=>t*t*(3-2*t);
}
