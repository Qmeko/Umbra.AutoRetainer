using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BSTExtra;

/// <summary>
/// このDLLは Dalamud だけを参照する。
/// RSR のDLLは起動時に探さないので、読み込みエラーにならない。
/// RSR が起動したあとで、同じフォルダの BSTExtra.Rotation.dll を足す。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly AssemblyLoadContext _loadContext;
    private bool _injected;
    private bool _resolveHooked;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog log)
    {
        _pluginInterface = pluginInterface;
        _framework = framework;
        _log = log;
        _loadContext = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)
                       ?? AssemblyLoadContext.Default;
        _framework.Update += OnUpdate;
        _log.Info("[BST Extra] 起動しました。Rotation Solver の準備を待ちます。");
    }

    private void OnUpdate(IFramework framework)
    {
        if (_injected)
        {
            return;
        }

        try
        {
            if (TryInject())
            {
                _injected = true;
                _framework.Update -= OnUpdate;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[BST Extra] 追加に失敗しました。");
            _injected = true;
            _framework.Update -= OnUpdate;
        }
    }

    private bool TryInject()
    {
        var rsAssembly = FindLoadedAssembly("RotationSolver");
        var basicAssembly = FindLoadedAssembly("RotationSolver.Basic");
        if (rsAssembly == null || basicAssembly == null)
        {
            return false;
        }

        HookResolve();

        var extraType = LoadExtraRotationType();
        if (extraType == null)
        {
            throw new InvalidOperationException("BSTExtra.Rotation.dll から BST_Extra を読めませんでした。");
        }

        var updaterType = rsAssembly.GetType("RotationSolver.Updaters.RotationUpdater");
        if (updaterType == null)
        {
            throw new InvalidOperationException("RotationUpdater が見つかりません。");
        }

        var rotationsProp = updaterType.GetProperty("CustomRotations", BindingFlags.Public | BindingFlags.Static);
        var lookupProp = updaterType.GetProperty("CustomRotationsLookup", BindingFlags.Public | BindingFlags.Static);
        var dictProp = updaterType.GetProperty("CustomRotationsDict", BindingFlags.Public | BindingFlags.Static);
        if (rotationsProp?.GetValue(null) is not Array groups || groups.Length < 22)
        {
            return false;
        }

        var jobType = FindLoadedAssembly("ECommons")?.GetType("ECommons.ExcelServices.Job")
                      ?? throw new InvalidOperationException("ECommons.Job が見つかりません。");
        var bstJob = Enum.Parse(jobType, "BST");

        var groupType = groups.GetValue(0)?.GetType()
                        ?? throw new InvalidOperationException("CustomRotationGroup が見つかりません。");
        var jobIdProp = groupType.GetProperty("JobId")
                        ?? throw new InvalidOperationException("JobId が見つかりません。");
        var classJobsProp = groupType.GetProperty("ClassJobIds")
                            ?? throw new InvalidOperationException("ClassJobIds が見つかりません。");
        var typesProp = groupType.GetProperty("Rotations")
                        ?? throw new InvalidOperationException("Rotations が見つかりません。");

        var newGroups = Array.CreateInstance(groupType, groups.Length);
        var foundBst = false;
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups.GetValue(i);
            if (group == null)
            {
                continue;
            }

            if (!foundBst && Equals(jobIdProp.GetValue(group), bstJob))
            {
                foundBst = true;
                var oldTypes = (Type[])typesProp.GetValue(group)!;
                if (oldTypes.Any(type => type == extraType))
                {
                    _log.Info("[BST Extra] すでに追加済みです。");
                    return true;
                }

                var merged = new Type[oldTypes.Length + 1];
                Array.Copy(oldTypes, merged, oldTypes.Length);
                merged[^1] = extraType;
                newGroups.SetValue(
                    Activator.CreateInstance(groupType, jobIdProp.GetValue(group), classJobsProp.GetValue(group), merged),
                    i);
                continue;
            }

            newGroups.SetValue(group, i);
        }

        if (!foundBst)
        {
            var grown = Array.CreateInstance(groupType, groups.Length + 1);
            Array.Copy(groups, grown, groups.Length);
            var jobArray = Array.CreateInstance(jobType, 1);
            jobArray.SetValue(bstJob, 0);
            grown.SetValue(
                Activator.CreateInstance(groupType, bstJob, jobArray, new[] { extraType }),
                groups.Length);
            newGroups = grown;
        }

        rotationsProp.SetValue(null, newGroups);
        if (lookupProp != null)
        {
            lookupProp.SetValue(null, Activator.CreateInstance(lookupProp.PropertyType));
        }

        UpdateDict(dictProp, groupType, jobIdProp, classJobsProp, typesProp, extraType, bstJob);
        _log.Info("[BST Extra] ローテ一覧に追加しました。/rsr の Rotations で BST Extra を選んでください。");
        return true;
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

        var rotationAssembly = _loadContext.LoadFromAssemblyPath(rotationPath);
        return rotationAssembly.GetType("RotationSolver.ExtraRotations.Melee.BST_Extra");
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

    private static void UpdateDict(
        PropertyInfo? dictProp,
        Type groupType,
        PropertyInfo jobIdProp,
        PropertyInfo classJobsProp,
        PropertyInfo typesProp,
        Type extraType,
        object bstJob)
    {
        if (dictProp?.GetValue(null) is not System.Collections.IDictionary dict)
        {
            return;
        }

        foreach (var key in dict.Keys)
        {
            if (dict[key] is not Array roleGroups)
            {
                continue;
            }

            for (var i = 0; i < roleGroups.Length; i++)
            {
                var group = roleGroups.GetValue(i);
                if (group == null || !Equals(jobIdProp.GetValue(group), bstJob))
                {
                    continue;
                }

                var oldTypes = (Type[])typesProp.GetValue(group)!;
                if (oldTypes.Any(type => type == extraType))
                {
                    return;
                }

                var merged = new Type[oldTypes.Length + 1];
                Array.Copy(oldTypes, merged, oldTypes.Length);
                merged[^1] = extraType;
                roleGroups.SetValue(
                    Activator.CreateInstance(groupType, jobIdProp.GetValue(group), classJobsProp.GetValue(group), merged),
                    i);
                return;
            }
        }
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
