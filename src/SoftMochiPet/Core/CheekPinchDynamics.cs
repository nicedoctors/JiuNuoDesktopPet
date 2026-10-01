namespace SoftMochiPet.Core;

public enum CheekSide { Left, Right }

public readonly record struct CheekPinchGeometry(
    double LeftX, double RightX, double CheekY, double HitRadiusX, double HitRadiusY,
    double MouthX = 0, double MouthY = 0)
{
    public static CheekPinchGeometry For(PetCharacterProfile character) =>
        character.UsesMischief
            ? new(133, 244, 239, 25, 18, 191, 241)
            : new(122, 239, 232, 25, 18, 178, 227);

    public CheekSide? HitTest(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
        var left = Math.Pow((x - LeftX) / HitRadiusX, 2) +
                   Math.Pow((y - CheekY) / HitRadiusY, 2);
        var right = Math.Pow((x - RightX) / HitRadiusX, 2) +
                    Math.Pow((y - CheekY) / HitRadiusY, 2);
        if (left > 1 && right > 1) return null;
        return left <= right ? CheekSide.Left : CheekSide.Right;
    }

    public double XFor(CheekSide side) => side == CheekSide.Left ? LeftX : RightX;
}

public sealed class CheekPinchDynamics
{
    public const double MaximumHorizontal = 105;
    public const double MaximumVertical = 42;

    private double _velocityX;
    private double _velocityY;
    private double _nibbleSeconds;
    private double _nibble;

    public bool Active { get; private set; }
    public bool Held { get; private set; }
    public CheekSide Side { get; private set; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public double TargetX { get; private set; }
    public double TargetY { get; private set; }
    public double Nibble => _nibble;
    public double OutwardTension => Math.Clamp(
        (Side == CheekSide.Left ? -X : X) / MaximumHorizontal, 0, 1);
    public double Tension => Math.Clamp(Math.Sqrt(
        X * X / (MaximumHorizontal * MaximumHorizontal) +
        Y * Y / (MaximumVertical * MaximumVertical)), 0, 1);

    public void Begin(CheekSide side)
    {
        Active = true;
        Held = true;
        Side = side;
        X = Y = TargetX = TargetY = 0;
        _velocityX = _velocityY = 0;
        _nibbleSeconds = 0;
        _nibble = 0;
    }

    public void Drag(double x, double y)
    {
        if (!Held || !double.IsFinite(x) || !double.IsFinite(y)) return;
        TargetX = Math.Clamp(x, -MaximumHorizontal, MaximumHorizontal);
        TargetY = Math.Clamp(y, -MaximumVertical, MaximumVertical);
    }

    public void Release()
    {
        if (!Held) return;
        Held = false;
        TargetX = TargetY = 0;
    }

    public void Reset()
    {
        Active = Held = false;
        X = Y = TargetX = TargetY = 0;
        _velocityX = _velocityY = 0;
        _nibbleSeconds = 0;
        _nibble = 0;
    }

    public void Tick(double delta)
    {
        if (!Active || !double.IsFinite(delta)) return;
        delta = Math.Clamp(delta, 0, 0.05);
        var omega = Held ? 20.0 : 17.0;
        var damping = Held ? 1.0 : 0.70;
        // Substeps keep the rebound stable even after a missed render frame.
        var steps = Math.Max(1, (int)Math.Ceiling(delta / 0.012));
        var step = delta / steps;
        for (var i = 0; i < steps; i++)
        {
            _velocityX += (omega * omega * (TargetX - X) - 2 * damping * omega * _velocityX) * step;
            _velocityY += (omega * omega * (TargetY - Y) - 2 * damping * omega * _velocityY) * step;
            X += _velocityX * step;
            Y += _velocityY * step;
        }
        _nibbleSeconds = Held && OutwardTension > 0.85 ? _nibbleSeconds + delta : 0;
        var nibbleTarget = _nibbleSeconds > .28 ? .55 + .45 * Math.Sin((_nibbleSeconds - .28) * Math.PI * 7) : 0;
        _nibble += (nibbleTarget - _nibble) * (1 - Math.Exp(-22 * delta));
        if (!Held && Math.Abs(X) < 0.2 && Math.Abs(Y) < 0.2 &&
            Math.Abs(_velocityX) < 1 && Math.Abs(_velocityY) < 1)
            Reset();
    }
}
