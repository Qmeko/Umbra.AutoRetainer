using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BSTExtra;

/// <summary>
/// RSR は本体DLLのローテしか読まない。
/// BST には公式ローテが無いので CurrentRotation が null のままだと、一覧UI自体が出ない。
/// このプラグインは BST Extra を毎フレーム確認し、消されていたら戻す。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly AssemblyLoadContext _loadContext;
    private bool _resolveHooked;
    private bool _announced;
    private Type? _extraType;
    private object? _extraInstance;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog log, IChatGui chat)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _log = log;
        _chat = chat;
        _loadContext = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)
                       ?? AssemblyLoadContext.Default;
        _framework.Update += OnUpdate;
        _log.Info("[BST Extra] 起動しました。Rotation Solver の準備を待ちます。");
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            TryEnsureRotation();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[BST Extra] 追加に失敗しました。");
        }
    }

    private void TryEnsureRotation()
    {
        var rsAssembly = FindLoadedAssembly("RotationSolver");
        var basicAssembly = FindLoadedAssembly("RotationSolver.Basic");
        if (rsAssembly == null || basicAssembly == null)
        {
            return;
        }

        HookResolve();
        _extraType ??= LoadExtraRotationType();
        if (_extraType == null)
        {
            return;
        }

        var updaterType = rsAssembly.GetType("RotationSolver.Updaters.RotationUpdater");
        if (updaterType == null)
        {
            return;
        }

        var rotationsProp = updaterType.GetProperty("CustomRotations", BindingFlags.Public | BindingFlags.Static);
        var lookupProp = updaterType.GetProperty("CustomRotationsLookup", BindingFlags.Public | BindingFlags.Static);
        if (rotationsProp?.GetValue(null) is not Array groups || groups.Length < 22)
        {
            return;
        }

        var jobType = FindLoadedAssembly("ECommons")?.GetType("ECommons.ExcelServices.Job");
        var combatTypeType = basicAssembly.GetType("RotationSolver.Basic.Data.CombatType");
        var icrType = basicAssembly.GetType("RotationSolver.Basic.Rotations.ICustomRotation");
        if (jobType == null || combatTypeType == null || icrType == null)
        {
            return;
        }

        var bstJob = Enum.Parse(jobType, "BST");
        var pve = Enum.Parse(combatTypeType, "PvE");
        EnsureGroupHasType(rotationsProp, groups, jobType, bstJob);
        EnsureLookupHasInstance(lookupProp, updaterType, jobType, combatTypeType, icrType, bstJob, pve);
    }

    private void EnsureGroupHasType(PropertyInfo rotationsProp, Array groups, Type jobType, object bstJob)
    {
        var groupType = groups.GetValue(0)?.GetType();
        if (groupType == null)
        {
            return;
        }

        var jobIdProp = groupType.GetProperty("JobId");
        var classJobsProp = groupType.GetProperty("ClassJobIds");
        var typesProp = groupType.GetProperty("Rotations");
        if (jobIdProp == null || classJobsProp == null || typesProp == null)
        {
            return;
        }

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups.GetValue(i);
            if (group == null || !Equals(jobIdProp.GetValue(group), bstJob))
            {
                continue;
            }

            var oldTypes = (Type[])typesProp.GetValue(group)!;
            if (oldTypes.Any(type => type == _extraType))
            {
                return;
            }

            var merged = new Type[oldTypes.Length + 1];
            Array.Copy(oldTypes, merged, oldTypes.Length);
            merged[^1] = _extraType!;
            var copy = Array.CreateInstance(groupType, groups.Length);
            Array.Copy(groups, copy, groups.Length);
            copy.SetValue(
                Activator.CreateInstance(groupType, jobIdProp.GetValue(group), classJobsProp.GetValue(group), merged),
                i);
            rotationsProp.SetValue(null, copy);
            return;
        }

        var grown = Array.CreateInstance(groupType, groups.Length + 1);
        Array.Copy(groups, grown, groups.Length);
        var jobArray = Array.CreateInstance(jobType, 1);
        jobArray.SetValue(bstJob, 0);
        grown.SetValue(Activator.CreateInstance(groupType, bstJob, jobArray, new[] { _extraType! }), groups.Length);
        rotationsProp.SetValue(null, grown);
    }

    private void EnsureLookupHasInstance(
        PropertyInfo? lookupProp,
        Type updaterType,
        Type jobType,
        Type combatTypeType,
        Type icrType,
        object bstJob,
        object pve)
    {
        if (lookupProp == null)
        {
            return;
        }

        _extraInstance ??= CreateExtraInstance();
        if (_extraInstance == null)
        {
            return;
        }

        if (lookupProp.GetValue(null) is not IDictionary lookup)
        {
            lookupProp.SetValue(null, Activator.CreateInstance(lookupProp.PropertyType));
            lookup = (IDictionary)lookupProp.GetValue(null)!;
        }

        if (!lookup.Contains(bstJob) || lookup[bstJob] is not IDictionary byCombat)
        {
            byCombat = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(combatTypeType, typeof(List<>).MakeGenericType(icrType)))!;
            lookup[bstJob] = byCombat;
        }

        var listType = typeof(List<>).MakeGenericType(icrType);
        if (!byCombat.Contains(pve) || byCombat[pve] is not IList list)
        {
            list = (IList)Activator.CreateInstance(listType)!;
            byCombat[pve] = list;
        }

        var already = false;
        foreach (var item in list)
        {
            if (item?.GetType() == _extraType)
            {
                already = true;
                break;
            }
        }

        if (!already)
        {
            list.Add(_extraInstance);
        }

        updaterType.GetMethod("ChangeRotation", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, [_extraInstance]);

        if (!_announced)
        {
            _announced = true;
            const string message = "[BST Extra] 追加しました。BSTのまま /rsr を開いて BST Extra を選んでください。";
            _log.Info(message);
            _chat.Print(message);
        }
    }

    private object? CreateExtraInstance()
    {
        try
        {
            return Activator.CreateInstance(_extraType!);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[BST Extra] BST_Extra の作成に失敗しました。");
            _chat.PrintError("[BST Extra] ローテの作成に失敗しました。/xllog を確認してください。");
            return null;
        }
    }

    private Type? LoadExtraRotationType()
    {
        var already = _loadContext.Assemblies.FirstOrDefault(a => a.GetName().Name == "BSTExtra.Rotation");
        if (already != null)
        {
            return already.GetType("RotationSolver.ExtraRotations.Melee.BST_Extra");
        }

        var directory = Path.GetDirectoryName(_pluginInterface.AssemblyLocation.FullName)
                        ?? AppContext.BaseDirectory;
        var rotationPath = Path.Combine(directory, "BSTExtra.Rotation.dll");
        if (!File.Exists(rotationPath))
        {
            throw new FileNotFoundException("BSTExtra.Rotation.dll が同じフォルダにありません。", rotationPath);
        }

        return _loadContext.LoadFromAssemblyPath(rotationPath)
            .GetType("RotationSolver.ExtraRotations.Melee.BST_Extra");
    }

    private void HookResolve()
    {
        if (_resolveHooked)
        {
            return;
        }

        _loadContext.Resolving += ResolveFromLoadedPlugins;
        _resolveHooked = true;
    }

    private static Assembly? ResolveFromLoadedPlugins(AssemblyLoadContext context, AssemblyName name)
    {
        return FindLoadedAssembly(name.Name);
    }

    private static Assembly? FindLoadedAssembly(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var context in AssemblyLoadContext.All)
        {
            foreach (var assembly in context.Assemblies)
            {
                if (assembly.GetName().Name == name)
                {
                    return assembly;
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        _framework.Update -= OnUpdate;
        if (_resolveHooked)
        {
            _loadContext.Resolving -= ResolveFromLoadedPlugins;
            _resolveHooked = false;
        }
    }
}
