using RotationSolver.Basic.Actions;
using RotationSolver.Basic.Attributes;
using RotationSolver.Basic.Data;
using RotationSolver.Basic.Rotations.Basic;

namespace RotationSolver.ExtraRotations.Melee;

/// <summary>
/// BSTの基本3コンボだけを回す Extra Rotation。
/// Smash Axe → Axeblade Bite → Shieldsplitter
/// 既存の BSM_Reborn は置き換えず、選択肢として追加する。
/// </summary>
[ExtraRotation]
[Rotation("BST Extra", CombatType.PvE, GameVersion = "7.56", Description = "Smash Axe combo only. Added locally, does not replace built-in rotations.")]
[SourceCode(Path = "BSTExtra/BST_Extra.cs")]
public sealed class BST_Extra : BeastmasterRotation
{
    private BaseAction? _smashAxe;
    private BaseAction? _axebladeBite;
    private BaseAction? _shieldsplitter;

    private BaseAction SmashAxe => _smashAxe ??= new((ActionID)44879);
    private BaseAction AxebladeBite
    {
        get
        {
            if (_axebladeBite == null)
            {
                _axebladeBite = new((ActionID)44883);
                _axebladeBite.Setting.ComboIds = [(ActionID)44879];
            }

            return _axebladeBite;
        }
    }

    private BaseAction Shieldsplitter
    {
        get
        {
            if (_shieldsplitter == null)
            {
                _shieldsplitter = new((ActionID)44885);
                _shieldsplitter.Setting.ComboIds = [(ActionID)44883];
            }

            return _shieldsplitter;
        }
    }

    /// <inheritdoc/>
    protected override bool GeneralGCD(out IAction? act)
    {
        if (Shieldsplitter.CanUse(out act))
        {
            return true;
        }

        if (AxebladeBite.CanUse(out act))
        {
            return true;
        }

        if (SmashAxe.CanUse(out act))
        {
            return true;
        }

        return base.GeneralGCD(out act);
    }
}
