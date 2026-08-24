namespace MiniMixerOverlay.Core;

/// <summary>
/// Zentrale Defaults fuer die einmalige Lautstaerke-Begrenzung neuer Anwendungen.
/// UI, Persistenz und Guard verwenden dieselbe Quelle, damit das Standardverhalten
/// nicht an mehreren Stellen auseinanderlaufen kann.
/// </summary>
public static class GuardDefaults
{
    public const int AutoVolumePercent = 5;
}
