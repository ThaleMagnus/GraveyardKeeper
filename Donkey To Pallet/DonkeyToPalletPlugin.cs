using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DonkeyToPallet
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class DonkeyToPalletPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "thalemagnus.graveyardkeeper.donkeytopallet";
        public const string PluginName = "Donkey To Pallet";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<StoragePriority> Priority;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Automatically route donkey-delivered corpses into an open morgue body-storage slot.");

            Priority = Config.Bind(
                "General",
                "Storage Priority",
                StoragePriority.RefrigeratedFirst,
                "Which body storage type should be filled first when both have an open slot.");

            DebugLogging = Config.Bind(
                "Advanced",
                "Debug Logging",
                false,
                "Write detailed corpse-routing information to the BepInEx log.");

            _harmony = new Harmony(PluginGuid);

            try
            {
                int patched = Patches.Install(_harmony);
                Logger.LogInfo(PluginName + " " + PluginVersion + " loaded. Priority: " + Priority.Value + ". Patched DropItem overloads: " + patched + ".");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to install donkey corpse routing patches: " + ex);
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }
    }

    public enum StoragePriority
    {
        RefrigeratedFirst,
        PalletsFirst
    }

    internal static class Patches
    {
        private static Type _worldGameObjectType;
        private static Type _worldMapType;
        private static readonly HashSet<int> SeenObjectIds = new HashSet<int>();

        internal static int Install(Harmony harmony)
        {
            _worldGameObjectType = AccessTools.TypeByName("WorldGameObject");
            _worldMapType = AccessTools.TypeByName("WorldMap");

            if (_worldGameObjectType == null)
                throw new TypeLoadException("Could not find Graveyard Keeper type WorldGameObject.");

            MethodInfo prefix = typeof(Patches).GetMethod("DropItemPrefix", BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                throw new MissingMethodException("DonkeyToPallet DropItemPrefix not found.");

            int patched = 0;
            foreach (MethodInfo method in _worldGameObjectType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "DropItem", StringComparison.Ordinal))
                    continue;

                if (method.IsAbstract || method.ContainsGenericParameters)
                    continue;

                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched++;
            }

            if (patched == 0)
                throw new MissingMethodException("No WorldGameObject.DropItem overloads were found.");

            return patched;
        }

        private static bool DropItemPrefix(MethodBase __originalMethod, object __instance, object[] __args)
        {
            try
            {
                if (!DonkeyToPalletPlugin.Enabled.Value)
                    return true;

                if (!IsDonkeyDropBodyCall())
                    return true;

                object corpse = FindCorpseArgument(__args);
                if (corpse == null)
                {
                    Debug("Flow_DropBody reached DropItem, but no corpse-like item argument was found in " + __originalMethod + ". Falling back to floor drop.");
                    return true;
                }

                object storage;
                if (!TryFindStorage(corpse, out storage))
                {
                    Debug("No open morgue body storage was found. Leaving the corpse on the floor.");
                    return true;
                }

                if (!TryInsert(storage, corpse))
                {
                    Debug("Selected storage rejected the corpse during insertion. Leaving the corpse on the floor.");
                    return true;
                }

                DonkeyToPalletPlugin.Log.LogInfo("Donkey corpse routed to " + Describe(storage) + ".");
                RefreshStorage(storage);

                // The corpse has already been inserted into body storage, so suppress the original floor drop.
                return false;
            }
            catch (Exception ex)
            {
                DonkeyToPalletPlugin.Log.LogError("Corpse routing failed; using the game's normal floor drop instead. " + ex);
                return true;
            }
        }

        private static bool IsDonkeyDropBodyCall()
        {
            StackFrame[] frames = new StackTrace(false).GetFrames();
            if (frames == null)
                return false;

            for (int i = 0; i < frames.Length; i++)
            {
                MethodBase method = frames[i].GetMethod();
                Type type = method == null ? null : method.DeclaringType;
                string fullName = type == null ? null : type.FullName;
                if (!string.IsNullOrEmpty(fullName) && fullName.IndexOf("Flow_DropBody", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static object FindCorpseArgument(object[] args)
        {
            if (args == null)
                return null;

            object fallback = null;

            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                if (arg == null)
                    continue;

                string text = Describe(arg).ToLowerInvariant();
                if (text.Contains("corpse") || text.Contains("body"))
                    return arg;

                Type type = arg.GetType();
                string typeName = type.FullName ?? type.Name;
                if (fallback == null &&
                    (typeName.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     typeName.IndexOf("Inventory", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    fallback = arg;
                }
            }

            return fallback;
        }

        private static bool TryFindStorage(object corpse, out object storage)
        {
            storage = null;

            List<object> refrigerators = new List<object>();
            List<object> pallets = new List<object>();

            SeenObjectIds.Clear();
            foreach (object candidate in EnumerateWorldObjects())
            {
                if (candidate == null || !_worldGameObjectType.IsInstanceOfType(candidate))
                    continue;

                int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(candidate);
                if (!SeenObjectIds.Add(identity))
                    continue;

                bool isBodyStorage;
                if (!TryReadBool(candidate, "is_body_storage", out isBodyStorage) || !isBodyStorage)
                    continue;

                if (!CanInsert(candidate, corpse))
                    continue;

                StorageKind kind = ClassifyStorage(candidate);
                if (kind == StorageKind.Refrigerated)
                    refrigerators.Add(candidate);
                else
                    pallets.Add(candidate);
            }

            Debug("Open body storage found: " + refrigerators.Count + " refrigerated, " + pallets.Count + " regular.");

            if (DonkeyToPalletPlugin.Priority.Value == StoragePriority.RefrigeratedFirst)
                storage = refrigerators.FirstOrDefault() ?? pallets.FirstOrDefault();
            else
                storage = pallets.FirstOrDefault() ?? refrigerators.FirstOrDefault();

            return storage != null;
        }

        private static IEnumerable<object> EnumerateWorldObjects()
        {
            // 1) Unity's full loaded-object table includes inactive GameObjects/components.
            UnityEngine.Object[] loaded = Resources.FindObjectsOfTypeAll(_worldGameObjectType);
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Length; i++)
                    yield return loaded[i];
            }

            // 2) Graveyard Keeper's WorldMap object lists can also contain world objects that are not active in scene hierarchy.
            if (_worldMapType == null)
                yield break;

            foreach (object map in EnumerateWorldMapInstances())
            {
                foreach (object obj in ReadObjectCollections(map, _worldMapType))
                    yield return obj;
            }

            foreach (object obj in ReadObjectCollections(null, _worldMapType))
                yield return obj;
        }

        private static IEnumerable<object> EnumerateWorldMapInstances()
        {
            UnityEngine.Object[] maps = Resources.FindObjectsOfTypeAll(_worldMapType);
            if (maps != null)
            {
                for (int i = 0; i < maps.Length; i++)
                    yield return maps[i];
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (FieldInfo field in _worldMapType.GetFields(flags))
            {
                if (!_worldMapType.IsAssignableFrom(field.FieldType))
                    continue;
                object value = SafeGet(() => field.GetValue(null));
                if (value != null)
                    yield return value;
            }

            foreach (PropertyInfo prop in _worldMapType.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0 || !_worldMapType.IsAssignableFrom(prop.PropertyType))
                    continue;
                object value = SafeGet(() => prop.GetValue(null, null));
                if (value != null)
                    yield return value;
            }
        }

        private static IEnumerable<object> ReadObjectCollections(object instance, Type ownerType)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 (instance == null ? BindingFlags.Static : BindingFlags.Instance);

            foreach (FieldInfo field in ownerType.GetFields(flags))
            {
                if (field.Name.IndexOf("obj", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                object value = SafeGet(() => field.GetValue(instance));
                foreach (object item in EnumerateValue(value))
                    yield return item;
            }

            foreach (PropertyInfo prop in ownerType.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0 || prop.Name.IndexOf("obj", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                object value = SafeGet(() => prop.GetValue(instance, null));
                foreach (object item in EnumerateValue(value))
                    yield return item;
            }
        }

        private static IEnumerable<object> EnumerateValue(object value)
        {
            if (value == null || value is string)
                yield break;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
                yield break;

            IEnumerator enumerator;
            try
            {
                enumerator = enumerable.GetEnumerator();
            }
            catch
            {
                yield break;
            }

            while (true)
            {
                object current;
                try
                {
                    if (!enumerator.MoveNext())
                        yield break;
                    current = enumerator.Current;
                }
                catch
                {
                    yield break;
                }

                if (current == null)
                    continue;

                if (current is DictionaryEntry)
                {
                    DictionaryEntry entry = (DictionaryEntry)current;
                    if (entry.Value != null)
                        yield return entry.Value;
                    continue;
                }

                Type t = current.GetType();
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    PropertyInfo valueProp = t.GetProperty("Value");
                    object kvValue = valueProp == null ? null : SafeGet(() => valueProp.GetValue(current, null));
                    if (kvValue != null)
                        yield return kvValue;
                    continue;
                }

                yield return current;
            }
        }

        private static StorageKind ClassifyStorage(object storage)
        {
            string text = Describe(storage).ToLowerInvariant();
            if (text.Contains("corpse_fridge") || text.Contains("fridge") || text.Contains("refriger"))
                return StorageKind.Refrigerated;

            return StorageKind.Regular;
        }

        private static bool CanInsert(object storage, object corpse)
        {
            MethodInfo method = FindCompatibleOneArgMethod(storage.GetType(), "CanInsertItem", corpse);
            if (method == null)
                return false;

            object result = SafeGet(() => method.Invoke(storage, new[] { corpse }));
            return result is bool && (bool)result;
        }

        private static bool TryInsert(object storage, object corpse)
        {
            MethodInfo method = FindCompatibleOneArgMethod(storage.GetType(), "AddToInventory", corpse);
            if (method == null)
                return false;

            object result;
            try
            {
                result = method.Invoke(storage, new[] { corpse });
            }
            catch (TargetInvocationException ex)
            {
                DonkeyToPalletPlugin.Log.LogError("AddToInventory threw: " + (ex.InnerException ?? ex));
                return false;
            }

            if (method.ReturnType == typeof(bool) && result is bool)
                return (bool)result;

            return true;
        }

        private static MethodInfo FindCompatibleOneArgMethod(Type type, string name, object arg)
        {
            Type argType = arg.GetType();
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                if (parameters[0].ParameterType.IsAssignableFrom(argType))
                    return method;
            }

            return null;
        }

        private static bool TryReadBool(object target, string name, out bool value)
        {
            value = false;
            if (target == null)
                return false;

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0 && prop.PropertyType == typeof(bool))
            {
                object result = SafeGet(() => prop.GetValue(target, null));
                if (result is bool)
                {
                    value = (bool)result;
                    return true;
                }
            }

            FieldInfo field = type.GetField(name, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                object result = SafeGet(() => field.GetValue(target));
                if (result is bool)
                {
                    value = (bool)result;
                    return true;
                }
            }

            return false;
        }

        private static string Describe(object obj)
        {
            if (obj == null)
                return "<null>";

            List<string> parts = new List<string>();
            parts.Add(obj.GetType().Name);

            UnityEngine.Object unityObj = obj as UnityEngine.Object;
            if (unityObj != null && !string.IsNullOrEmpty(unityObj.name))
                parts.Add(unityObj.name);

            string[] memberNames =
            {
                "obj_id", "object_id", "id", "item_id", "name", "prefab_name", "custom_tag", "type"
            };

            for (int i = 0; i < memberNames.Length; i++)
            {
                object value = ReadMember(obj, memberNames[i]);
                if (value == null)
                    continue;
                string s = value as string;
                if (!string.IsNullOrEmpty(s))
                    parts.Add(s);
            }

            object data = ReadMember(obj, "data") ?? ReadMember(obj, "definition") ?? ReadMember(obj, "config");
            if (data != null && !ReferenceEquals(data, obj))
            {
                for (int i = 0; i < memberNames.Length; i++)
                {
                    object value = ReadMember(data, memberNames[i]);
                    string s = value as string;
                    if (!string.IsNullOrEmpty(s))
                        parts.Add(s);
                }
            }

            return string.Join(" | ", parts.Distinct().ToArray());
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null)
                return null;

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo field = type.GetField(name, flags);
            if (field != null)
                return SafeGet(() => field.GetValue(target));

            PropertyInfo prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanRead && prop.GetIndexParameters().Length == 0)
                return SafeGet(() => prop.GetValue(target, null));

            return null;
        }

        private static void RefreshStorage(object storage)
        {
            string[] methods = { "Redraw", "Refresh", "UpdateVisual", "UpdateState", "UpdateMesh" };
            Type type = storage.GetType();

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = type.GetMethod(methods[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(storage, null);
                }
                catch
                {
                    // Cosmetic refresh only. The inventory transfer already succeeded.
                }
            }
        }

        private static object SafeGet(Func<object> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }

        private static void Debug(string message)
        {
            if (DonkeyToPalletPlugin.DebugLogging.Value)
                DonkeyToPalletPlugin.Log.LogInfo("[Debug] " + message);
        }

        private enum StorageKind
        {
            Regular,
            Refrigerated
        }
    }
}
