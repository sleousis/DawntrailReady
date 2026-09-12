// Port of TexTools xivModdingFramework/Mods/EndwalkerUpgrade.cs (commit 25b3ae72) SkinRepathDict, verbatim.
namespace DawntrailReady.Core.Upgrade;

public sealed partial class EndwalkerUpgrade
{
    public static readonly Dictionary<string, string> SkinRepathDict = new Dictionary<string, string>()
    {
        // Base Game
        { "chara/human/c0201/obj/body/b0001/texture/--c0201b0001_d.tex", "chara/human/c0201/obj/body/b0001/texture/c0201b0001_base.tex" },
        { "chara/human/c0401/obj/body/b0001/texture/--c0401b0001_d.tex", "chara/human/c0401/obj/body/b0001/texture/c0401b0001_base.tex" },
        { "chara/human/c1401/obj/body/b0001/texture/--c1401b0001_d.tex", "chara/human/c1401/obj/body/b0001/texture/c1401b0001_base.tex" },
        { "chara/human/c1401/obj/body/b0101/texture/--c1401b0101_d.tex", "chara/human/c1401/obj/body/b0101/texture/c1401b0101_base.tex" },
        { "chara/human/c1801/obj/body/b0001/texture/--c1801b0001_d.tex", "chara/human/c1801/obj/body/b0001/texture/c1801b0001_base.tex" },

        { "chara/human/c0101/obj/body/b0001/texture/--c0101b0001_d.tex", "chara/human/c0101/obj/body/b0001/texture/c0101b0001_base.tex" },
        { "chara/human/c0301/obj/body/b0001/texture/--c0301b0001_d.tex", "chara/human/c0301/obj/body/b0001/texture/c0301b0001_base.tex" },
        { "chara/human/c1301/obj/body/b0001/texture/--c1301b0001_d.tex", "chara/human/c1301/obj/body/b0001/texture/c1301b0001_base.tex" },
        { "chara/human/c1301/obj/body/b0101/texture/--c1301b0101_d.tex", "chara/human/c1301/obj/body/b0101/texture/c1301b0101_base.tex" },
        { "chara/human/c1701/obj/body/b0001/texture/--c1701b0001_d.tex", "chara/human/c1701/obj/body/b0001/texture/c1701b0001_base.tex" },

        // Bibo
        { "chara/bibo/midlander_d.tex", "chara/bibo_mid_base.tex" },
        { "chara/bibo/raen_d.tex", "chara/bibo_raen_base.tex" },
        { "chara/bibo/xaela_d.tex", "chara/bibo_xaela_base.tex" },
        { "chara/bibo/viera_d.tex", "chara/bibo_viera_base.tex" },
        { "chara/bibo/highlander_d.tex", "chara/bibo_high_base.tex" },

        // TBSE
        { "chara/human/c0101/obj/body/b0001/texture/--c0101b0001_b_d.tex", "chara/human/c0101/obj/body/b0001/texture/c0101b0001_b_d.tex" },
        { "chara/human/c1301/obj/body/b0001/texture/--c1301b0001_b_d.tex", "chara/human/c1301/obj/body/b0001/texture/c1301b0001_b_d.tex" },
        { "chara/human/c1301/obj/body/b0101/texture/--c1301b0101_b_d.tex", "chara/human/c1301/obj/body/b0101/texture/c1301b0101_b_d.tex" },
        { "chara/human/c1701/obj/body/b0001/texture/--c1701b0001_b_d.tex", "chara/human/c1701/obj/body/b0001/texture/c1701b0001_b_d.tex" },
        { "chara/human/c0301/obj/body/b0001/texture/--c0301b0001_b_d.tex", "chara/human/c0301/obj/body/b0001/texture/c0301b0001_b_d.tex" },

        // Au Ra Tails
        { "chara/human/c1301/obj/tail/t0001/texture/--c1301t0001_etc_d.tex", "chara/human/c1301/obj/tail/t0001/texture/c1301t0001_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0002/texture/--c1301t0002_etc_d.tex", "chara/human/c1301/obj/tail/t0002/texture/c1301t0002_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0003/texture/--c1301t0003_etc_d.tex", "chara/human/c1301/obj/tail/t0003/texture/c1301t0003_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0004/texture/--c1301t0004_etc_d.tex", "chara/human/c1301/obj/tail/t0004/texture/c1301t0004_etc_base.tex" },

        { "chara/human/c1301/obj/tail/t0101/texture/--c1301t0101_etc_d.tex", "chara/human/c1301/obj/tail/t0101/texture/c1301t0101_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0102/texture/--c1301t0102_etc_d.tex", "chara/human/c1301/obj/tail/t0102/texture/c1301t0102_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0103/texture/--c1301t0103_etc_d.tex", "chara/human/c1301/obj/tail/t0103/texture/c1301t0103_etc_base.tex" },
        { "chara/human/c1301/obj/tail/t0104/texture/--c1301t0104_etc_d.tex", "chara/human/c1301/obj/tail/t0104/texture/c1301t0104_etc_base.tex" },

        { "chara/human/c1401/obj/tail/t0001/texture/--c1401t0001_etc_d.tex", "chara/human/c1401/obj/tail/t0001/texture/c1401t0001_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0002/texture/--c1401t0002_etc_d.tex", "chara/human/c1401/obj/tail/t0002/texture/c1401t0002_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0003/texture/--c1401t0003_etc_d.tex", "chara/human/c1401/obj/tail/t0003/texture/c1401t0003_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0004/texture/--c1401t0004_etc_d.tex", "chara/human/c1401/obj/tail/t0004/texture/c1401t0004_etc_base.tex" },

        { "chara/human/c1401/obj/tail/t0101/texture/--c1401t0101_etc_d.tex", "chara/human/c1401/obj/tail/t0101/texture/c1401t0101_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0102/texture/--c1401t0102_etc_d.tex", "chara/human/c1401/obj/tail/t0102/texture/c1401t0102_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0103/texture/--c1401t0103_etc_d.tex", "chara/human/c1401/obj/tail/t0103/texture/c1401t0103_etc_base.tex" },
        { "chara/human/c1401/obj/tail/t0104/texture/--c1401t0104_etc_d.tex", "chara/human/c1401/obj/tail/t0104/texture/c1401t0104_etc_base.tex" },

        // (TexTools keeps the normal-map repaths commented out.)
    };
}
