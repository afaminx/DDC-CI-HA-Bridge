namespace SolarMonitorBrightness;

internal static class BrightnessMapper
{
    public static int MapLuxToBrightness(decimal lux, BrightnessCurve curve, decimal referenceLux)
    {
        curve.Normalize(referenceLux);
        var points = curve.Points;
        var luxPercent = Math.Clamp(lux / Math.Clamp(referenceLux, 1, 1000000) * 100, 0, 100);

        if (luxPercent <= points[0].Lux)
        {
            return Math.Clamp(points[0].Brightness, 1, 100);
        }

        if (luxPercent >= points[^1].Lux)
        {
            return Math.Clamp(points[^1].Brightness, 1, 100);
        }

        for (var index = 0; index < points.Count - 1; index++)
        {
            var left = points[index];
            var right = points[index + 1];
            if (luxPercent < left.Lux || luxPercent > right.Lux)
            {
                continue;
            }

            if (left.Lux == right.Lux)
            {
                return Math.Clamp(right.Brightness, 1, 100);
            }

            var position = (luxPercent - left.Lux) / (right.Lux - left.Lux);
            var brightness = left.Brightness + position * (right.Brightness - left.Brightness);
            return Math.Clamp((int)Math.Round(brightness, MidpointRounding.AwayFromZero), 1, 100);
        }

        return Math.Clamp(points[^1].Brightness, 1, 100);
    }
}
