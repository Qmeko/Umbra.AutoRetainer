using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace BSTExtra;

/// <summary>
/// 別DLLを RSR の collectible ALC に読み込むと
/// "Resolving to a collectible assembly is not supported" になる。
/// そのため RotationSolver と同じ読み込み領域の中で
/// BeastmasterRotation の子クラスを動的生成して追加する。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Version = "1.0.4";
    private const string DynamicAssemblyName = "BSTExtra.Dynamic";
    private const string ExtraTypeName = "RotationSolver.ExtraRotations.Melee.BST_Extra";

    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string[] GeneratedActionNames =
    [
        "ShieldsplitterPvE",
        "AxebladeBitePvE",
        "SmashAxePvE",
    ];

    private static readonly (uint Id, uint? ComboFrom)[] ComboFallback =
    [
        (44885, 44883),
        (44883, 44879),
        (44879, null),
    ];

    private static readonly ConditionalWeakTable<object, Dictionary<uint, object>> ActionCache = [];

    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;
    private readonly string _loadedFrom;
    private bool _announced;
    private bool _loggedError;
    private Type? _extraType;
    private object? _extraInstance;

    public Plugin(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog log, IChatGui chat)
    {
        _framework = framework;
        _log = log;
        _chat = chat;
        _loadedFrom = pluginInterface.AssemblyLocation.FullName;
        _framework.Update += OnUpdate;

        log.Info($"[BST Extra] v{Version} 起動 {_loadedFrom}");
        chat.Print($"[BST Extra] v{Version} を読み込みました。");
        if (LooksLikeLocalRepoBuild(_loadedFrom))
        {
            log.Warning($"[BST Extra] リポジトリ直下の古いDLLです。XIVLauncher\\devPlugins\\BSTExtra のコピーだけを使ってください。 path={_loadedFrom}");
            chat.PrintError("[BST Extra] 古い場所から読み込まれています。devPlugins\\BSTExtra のコピーだけを使ってください。");
        }
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            TryEnsureRotation();
        }
        catch (Exception ex)
        {
            if (_loggedError)
            {
                return;
            }

            _loggedError = true;
            _log.Error(ex, $"[BST Extra] v{Version} 追加に失敗しました。 path={_loadedFrom}");
            _chat.PrintError($"[BST Extra] v{Version} 追加に失敗しました。/xllog を確認してください。");
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

        _extraType ??= EmitRotationType(rsAssembly, basicAssembly);

        var updaterType = rsAssembly.GetType("RotationSolver.Updaters.RotationUpdater")
                          ?? throw new InvalidOperationException("RotationUpdater が見つかりません。");
        var rotationsProp = updaterType.GetProperty("CustomRotations", StaticFlags)
                            ?? throw new InvalidOperationException("CustomRotations が見つかりません。");
        var lookupProp = updaterType.GetProperty("CustomRotationsLookup", StaticFlags);

        if (rotationsProp.GetValue(null) is not Array groups || groups.Length < 22)
        {
            return;
        }

        var jobType = FindLoadedAssembly("ECommons")?.GetType("ECommons.ExcelServices.Job")
                      ?? throw new InvalidOperationException("ECommons.Job が見つかりません。");
        var combatTypeType = basicAssembly.GetType("RotationSolver.Basic.Data.CombatType")
                             ?? throw new InvalidOperationException("CombatType が見つかりません。");
        var icrType = basicAssembly.GetType("RotationSolver.Basic.Rotations.ICustomRotation")
                      ?? throw new InvalidOperationException("ICustomRotation が見つかりません。");

        var bstJob = Enum.Parse(jobType, "BST");
        var pve = Enum.Parse(combatTypeType, "PvE");
        EnsureGroupHasType(rotationsProp, groups, jobType, bstJob);
        EnsureLookupHasInstance(lookupProp, updaterType, basicAssembly, combatTypeType, icrType, bstJob, pve);
    }

    private Type EmitRotationType(Assembly rsAssembly, Assembly basicAssembly)
    {
        var rsrContext = AssemblyLoadContext.GetLoadContext(basicAssembly)
                         ?? AssemblyLoadContext.GetLoadContext(rsAssembly)
                         ?? throw new InvalidOperationException("RSR の読み込み領域が見つかりません。");

        foreach (var assembly in rsrContext.Assemblies)
        {
            if (assembly.GetName().Name == DynamicAssemblyName)
            {
                return assembly.GetType(ExtraTypeName)
                       ?? throw new InvalidOperationException("動的ローテ型が見つかりません。");
            }
        }

        var baseType = basicAssembly.GetType("RotationSolver.Basic.Rotations.Basic.BeastmasterRotation", true)!;
        var rotationAttrType = basicAssembly.GetType("RotationSolver.Basic.Attributes.RotationAttribute", true)!;
        var extraAttrType = basicAssembly.GetType("RotationSolver.Basic.Attributes.ExtraRotationAttribute", true)!;
        var combatTypeType = basicAssembly.GetType("RotationSolver.Basic.Data.CombatType", true)!;
        var iActionType = basicAssembly.GetType("RotationSolver.Basic.Actions.IAction", true)!;
        var pve = Enum.Parse(combatTypeType, "PvE");

        Type created;
        using (rsrContext.EnterContextualReflection())
        {
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(DynamicAssemblyName), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(DynamicAssemblyName);
            var typeBuilder = moduleBuilder.DefineType(
                ExtraTypeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                baseType);

            ApplyRotationAttribute(typeBuilder, rotationAttrType, pve);
            typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(extraAttrType.GetConstructor(Type.EmptyTypes)!, []));
            TryApplyJobsAttribute(typeBuilder, basicAssembly);

            var hookField = typeBuilder.DefineField(
                "GcdHook",
                typeof(Func<object, object>),
                FieldAttributes.Public | FieldAttributes.Static);

            var baseGcd = FindGeneralGcd(baseType) ?? throw new InvalidOperationException("GeneralGCD が見つかりません。");
            var method = typeBuilder.DefineMethod(
                "GeneralGCD",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(bool),
                [iActionType.MakeByRefType()]);
            method.DefineParameter(1, ParameterAttributes.Out, "act");

            var il = method.GetILGenerator();
            var arrayLocal = il.DeclareLocal(typeof(object[]));
            il.Emit(OpCodes.Ldsfld, hookField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, typeof(Func<object, object>).GetMethod("Invoke")!);
            il.Emit(OpCodes.Castclass, typeof(object[]));
            il.Emit(OpCodes.Stloc, arrayLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, iActionType);
            il.Emit(OpCodes.Stind_Ref);
            il.Emit(OpCodes.Ldloc, arrayLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, typeof(bool));
            il.Emit(OpCodes.Ret);
            typeBuilder.DefineMethodOverride(method, baseGcd);

            created = typeBuilder.CreateType() ?? throw new InvalidOperationException("動的ローテ型の生成に失敗しました。");
        }

        if (AssemblyLoadContext.GetLoadContext(created.Assembly) != rsrContext)
        {
            throw new InvalidOperationException("動的ローテが RSR の読み込み領域に入りませんでした。");
        }

        created.GetField("GcdHook", BindingFlags.Public | BindingFlags.Static)!
            .SetValue(null, new Func<object, object>(RunGeneralGcd));
        _log.Info($"[BST Extra] v{Version} 動的ローテを RSR 領域に生成しました。 alc={rsrContext.Name}");
        return created;
    }

    private static void ApplyRotationAttribute(TypeBuilder typeBuilder, Type rotationAttrType, object pve)
    {
        var rotationCtor = rotationAttrType.GetConstructors(InstanceFlags).First(ctor =>
        {
            var parameters = ctor.GetParameters();
            return parameters.Length >= 2 && parameters[0].ParameterType == typeof(string);
        });

        var parameters = rotationCtor.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = "BST Extra";
        args[1] = pve;
        for (var i = 2; i < parameters.Length; i++)
        {
            args[i] = parameters[i].HasDefaultValue
                ? parameters[i].DefaultValue
                : (parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null);
        }

        var namedProperties = new List<PropertyInfo>();
        var namedValues = new List<object>();
        var gameVersion = rotationAttrType.GetProperty("GameVersion");
        var description = rotationAttrType.GetProperty("Description");
        if (gameVersion?.SetMethod != null)
        {
            namedProperties.Add(gameVersion);
            namedValues.Add("7.56");
        }

        if (description?.SetMethod != null)
        {
            namedProperties.Add(description);
            namedValues.Add("Smash Axe combo only.");
        }

        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(
            rotationCtor,
            args!,
            namedProperties.ToArray(),
            namedValues.ToArray()));
    }

    private static void TryApplyJobsAttribute(TypeBuilder typeBuilder, Assembly basicAssembly)
    {
        var jobsAttrType = basicAssembly.GetType("RotationSolver.Basic.Attributes.JobsAttribute");
        var jobType = FindLoadedAssembly("ECommons")?.GetType("ECommons.ExcelServices.Job");
        if (jobsAttrType == null || jobType == null)
        {
            return;
        }

        var ctor = jobsAttrType.GetConstructors(InstanceFlags).FirstOrDefault();
        if (ctor == null)
        {
            return;
        }

        var jobs = Array.CreateInstance(jobType, 1);
        jobs.SetValue(Enum.Parse(jobType, "BST"), 0);
        typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(ctor, [jobs]));
    }

    private static MethodInfo? FindGeneralGcd(Type baseType)
    {
        for (var type = baseType; type != null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(InstanceFlags | BindingFlags.DeclaredOnly))
            {
                var parameters = method.GetParameters();
                if (method.Name == "GeneralGCD" && parameters.Length == 1 && parameters[0].ParameterType.IsByRef)
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static object RunGeneralGcd(object self)
    {
        foreach (var action in EnumerateComboActions(self))
        {
            if (TryCanUse(action, out var used))
            {
                return new object?[] { true, used };
            }
        }

        return new object?[] { false, null };
    }

    private static IEnumerable<object> EnumerateComboActions(object self)
    {
        foreach (var name in GeneratedActionNames)
        {
            var property = self.GetType().GetProperty(name, InstanceFlags);
            var value = property?.GetValue(self);
            if (value != null)
            {
                yield return value;
            }
        }

        if (GeneratedActionNames.Any(name => self.GetType().GetProperty(name, InstanceFlags)?.GetValue(self) != null))
        {
            yield break;
        }

        foreach (var (id, comboFrom) in ComboFallback)
        {
            yield return GetOrCreateAction(self, id, comboFrom);
        }
    }

    private static object GetOrCreateAction(object self, uint actionId, uint? comboFrom)
    {
        var cache = ActionCache.GetOrCreateValue(self);
        if (cache.TryGetValue(actionId, out var cached))
        {
            return cached;
        }

        var basicAssembly = FindLoadedAssembly("RotationSolver.Basic")
                            ?? throw new InvalidOperationException("RotationSolver.Basic が見つかりません。");
        var actionIdType = basicAssembly.GetType("RotationSolver.Basic.Data.ActionID", true)!;
        var baseActionType = basicAssembly.GetType("RotationSolver.Basic.Actions.BaseAction", true)!;
        var ctor = baseActionType.GetConstructor([actionIdType, typeof(bool)])
                   ?? baseActionType.GetConstructor([actionIdType])
                   ?? throw new InvalidOperationException("BaseAction のコンストラクタが見つかりません。");
        var created = ctor.GetParameters().Length == 2
            ? ctor.Invoke([Enum.ToObject(actionIdType, actionId), false])
            : ctor.Invoke([Enum.ToObject(actionIdType, actionId)]);

        if (comboFrom != null)
        {
            var setting = baseActionType.GetProperty("Setting")?.GetValue(created);
            var comboProp = setting?.GetType().GetProperty("ComboIds");
            if (setting != null && comboProp != null)
            {
                var combo = Array.CreateInstance(actionIdType, 1);
                combo.SetValue(Enum.ToObject(actionIdType, comboFrom.Value), 0);
                comboProp.SetValue(setting, combo);
            }
        }

        cache[actionId] = created;
        return created;
    }

    private static bool TryCanUse(object action, out object? used)
    {
        used = null;
        var method = action.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => candidate.Name == "CanUse" && candidate.GetParameters().Length > 0);
        if (method == null)
        {
            return false;
        }

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 1; i < parameters.Length; i++)
        {
            if (parameters[i].HasDefaultValue)
            {
                args[i] = parameters[i].DefaultValue;
            }
            else if (parameters[i].ParameterType.IsEnum)
            {
                args[i] = Activator.CreateInstance(parameters[i].ParameterType);
            }
            else if (parameters[i].ParameterType == typeof(bool))
            {
                args[i] = false;
            }
            else if (parameters[i].ParameterType == typeof(byte))
            {
                args[i] = (byte)0;
            }
        }

        var ok = (bool)method.Invoke(action, args)!;
        used = args[0];
        return ok;
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
            copy.SetValue(CreateGroup(groupType, jobIdProp.GetValue(group), classJobsProp.GetValue(group), merged), i);
            rotationsProp.SetValue(null, copy);
            return;
        }

        var grown = Array.CreateInstance(groupType, groups.Length + 1);
        Array.Copy(groups, grown, groups.Length);
        var jobArray = Array.CreateInstance(jobType, 1);
        jobArray.SetValue(bstJob, 0);
        grown.SetValue(CreateGroup(groupType, bstJob, jobArray, new[] { _extraType! }), groups.Length);
        rotationsProp.SetValue(null, grown);
    }

    private static object CreateGroup(Type groupType, object? jobId, object? classJobs, Type[] rotations)
    {
        return Activator.CreateInstance(
            groupType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [jobId, classJobs, rotations],
            null)!;
    }

    private void EnsureLookupHasInstance(
        PropertyInfo? lookupProp,
        Type updaterType,
        Assembly basicAssembly,
        Type combatTypeType,
        Type icrType,
        object bstJob,
        object pve)
    {
        if (lookupProp == null)
        {
            throw new InvalidOperationException("CustomRotationsLookup が見つかりません。");
        }

        _extraInstance ??= Activator.CreateInstance(_extraType!);
        if (_extraInstance == null)
        {
            throw new InvalidOperationException("BST Extra のインスタンスを作れませんでした。");
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

        if (!byCombat.Contains(pve) || byCombat[pve] is not IList list)
        {
            list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(icrType))!;
            byCombat[pve] = list;
        }

        if (list.Cast<object?>().All(item => item?.GetType() != _extraType))
        {
            list.Add(_extraInstance);
        }

        var dataCenterType = basicAssembly.GetType("RotationSolver.Basic.DataCenter");
        var current = dataCenterType?.GetProperty("CurrentRotation", StaticFlags)?.GetValue(null);
        if (current == null)
        {
            updaterType.GetMethod("ChangeRotation", StaticFlags)?.Invoke(null, [_extraInstance]);
        }

        if (!_announced)
        {
            _announced = true;
            var message = $"[BST Extra] v{Version} 追加しました。BSTのまま /rsr を開いて、紫の BST Extra を選んでください。";
            _log.Info($"{message} path={_loadedFrom}");
            _chat.Print(message);
        }
    }

    private static bool LooksLikeLocalRepoBuild(string path)
    {
        return path.Contains("Umbra.AutoRetainer", StringComparison.OrdinalIgnoreCase)
               && !path.Contains($"{Path.DirectorySeparatorChar}devPlugins{Path.DirectorySeparatorChar}BSTExtra", StringComparison.OrdinalIgnoreCase)
               && !path.Contains("/devPlugins/BSTExtra", StringComparison.OrdinalIgnoreCase);
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
    }
}
