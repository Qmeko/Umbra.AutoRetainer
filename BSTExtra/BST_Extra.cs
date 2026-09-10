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
    private readonly BaseAction _smashAxe = new((ActionID)44879);
    private readonly BaseAction _axebladeBite = new((ActionID)44883);
    private readonly BaseAction _shieldsplitter = new((ActionID)44885);

    public BST_Extra()
    {
        _axebladeBite.Setting.ComboIds = [(ActionID)44879];
        _shieldsplitter.Setting.ComboIds = [(ActionID)44883];
    }

    /// <inheritdoc/>
    protected override bool GeneralGCD(out IAction? act)
    {
        if (_shieldsplitter.CanUse(out act))
        {
            return true;
        }

        if (_axebladeBite.CanUse(out act))
        {
            return true;
        }

        if (_smashAxe.CanUse(out act))
        {
            return true;
        }

        return base.GeneralGCD(out act);
    }
}
