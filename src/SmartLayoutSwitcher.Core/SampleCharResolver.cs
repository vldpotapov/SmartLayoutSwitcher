namespace SmartLayoutSwitcher.Core;

/// <summary>
/// Picks the sample letter shown under each language chip in the layout popup
/// (spec §34: "A / Б / Č"). Pure lookup so it can be tested.
/// </summary>
public static class SampleCharResolver
{
    public static char ForLcid(ushort lcid) => lcid switch
    {
        // English variants
        0x0409 or 0x0809 or 0x0C09 or 0x1009 or 0x1409 or 0x1809 or 0x1C09 or 0x2009 or 0x2409
            or 0x2809 or 0x2C09 or 0x3009 or 0x3409 or 0x3809 or 0x3C09 or 0x4009 => 'A',
        // Czech
        0x0405 => 'Č',
        // Russian
        0x0419 or 0x0819 => 'Б',
        // German
        0x0407 => 'Ä',
        0x0807 => 'Ö',
        // French
        0x040C => 'É',
        0x080C => 'È',
        0x0C0C => 'Ç',
        // Spanish
        0x040A or 0x080A or 0x0C0A or 0x100A or 0x140A => 'Ñ',
        // Italian
        0x0410 => 'À',
        // Portuguese
        0x0416 => 'Ã',
        // Ukrainian
        0x0422 => 'Ї',
        // Polish
        0x0415 => 'Ł',
        // Turkish
        0x041F => 'Ğ',
        // Japanese
        0x0411 or 0x0811 => 'あ',
        // Greek
        0x0408 => 'Ω',
        _ => 'A',
    };

    public static string ThreeLetterCode(ushort lcid) => lcid switch
    {
        0x0409 or 0x0809 or 0x0C09 or 0x1009 or 0x1409 or 0x1809 or 0x1C09 or 0x2009 or 0x2409
            or 0x2809 or 0x2C09 or 0x3009 or 0x3409 or 0x3809 or 0x3C09 or 0x4009 => "ENG",
        0x0405 => "CES",
        0x0419 or 0x0819 => "RUS",
        0x0407 or 0x0807 => "DEU",
        0x040C or 0x080C or 0x0C0C => "FRA",
        0x040A or 0x080A or 0x0C0A or 0x100A or 0x140A => "ESP",
        0x0410 => "ITA",
        0x0416 => "POR",
        0x0422 => "UKR",
        0x0415 => "POL",
        0x041F => "TUR",
        0x0411 or 0x0811 => "JPN",
        0x0408 => "ELL",
        _ => "LNG",
    };
}