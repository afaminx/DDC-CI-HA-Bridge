namespace SolarMonitorBrightness;

internal static class BrightnessMapper
{
    public static int MapLuxToBrightness(decimal lux, AppSettings settings)
    {
        var minLux = settings.LuxAtMinimumBrightness;
        var maxLux = settings.LuxAtMaximumBrightness;
        var minBrightness = Math.Clamp(settings.MinimumMonitorBrightness, 1, 100);
        var maxBrightness = Math.Clamp(settings.MaximumMonitorBrightness, 1, 100);

        if (minBrightness > maxBrightness)
        {
            (minBrightness, maxBrightness) = (maxBrightness, minBrightness);
        }

        if (minLux == maxLux)
        {
            return Math.Clamp(minBrightness, 1, 100);
        }

        var position = (lux - minLux) / (maxLux - minLux);
        position = Math.Clamp(position, 0, 1);

        var brightness = minBrightness + position * (maxBrightness - minBrightness);
        return Math.Clamp((int)Math.Round(brightness, MidpointRounding.AwayFromZero), 1, 100);
    }
}
