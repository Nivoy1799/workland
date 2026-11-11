using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishNet.Broadcast;
using FishNet.Object;

namespace Worktown.Game.Net
{
    /// <summary>
    /// Spec 047: the game's network surface as text lines, for the build hash (Rules.BuildHash): every RPC of every NetworkBehaviour
    /// (server, observers, target, replicate, reconcile) with its parameter types, every synced field with its type, and every
    /// broadcast with its fields. Read by reflection from this assembly, so two builds of the same code give the same lines on any PC
    /// and a changed message changes the hash. Type names are written without assembly versions (they differ between the Editor and
    /// a player). The fields of a struct used as an RPC parameter or a synced type are not expanded (only its type name is listed);
    /// only broadcasts list their fields.
    /// Enum values are not hashed either (a field lists only its type name, and the ConnectionNotice broadcast sends its
    /// DisconnectReason as a plain byte): a new value, such as spec 049's DisconnectReason.HostStalled, keeps the hash, so an older
    /// build can receive a value it does not know and must handle it (ConnectionMessages.Unknown).
    /// </summary>
    public static class ProtocolSurface
    {
        /// <summary>
        /// 054 review: the wire revision, hashed as the line "revision N". The lines see RPCs, synced fields and broadcasts, but not how a
        /// prefab is put together: the order of its NetworkBehaviours (their indexes on the wire), its NetworkObject's prediction and
        /// state forwarding flags, or a NetworkTransform's settings that change what it sends. Every such wire change bumps this number,
        /// so an older and a newer build refuse each other cleanly. 1: up to spec 047 (no line). 2: spec 054 (the player root's
        /// NetworkTransform, no state forwarding, items silent while held, no scale sync; 054 review: item teleports). 3: the 053 review
        /// (one ShopLedger balance instead of a Wallet SyncVar per player; players pass through each other by default). 4: spec 037 (D39:
        /// every money value on the wire is cents, not whole dollars: the ShopLedger balance, the goal, the register's change due and its
        /// tray (now a ushort), the reward's and the shift's money, the menu board's prices; the reflection sees the new types, not the unit).
        /// Every wire change bumps it (docs/architecture.md).
        /// </summary>
        public const int Revision = 4;

        /// <summary>The line that carries <see cref="Revision"/>.</summary>
        public static string RevisionLine => "revision " + Revision;

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        private static readonly string[] RpcAttributes =
            { "ServerRpcAttribute", "ObserversRpcAttribute", "TargetRpcAttribute", "ReplicateAttribute", "ReconcileAttribute" };

        /// <summary>The lines of <paramref name="assembly"/> (unsorted; BuildHash sorts).</summary>
        public static List<string> Lines(Assembly assembly)
        {
            var lines = new List<string>();
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
            }
            foreach (Type type in types)
            {
                if (typeof(NetworkBehaviour).IsAssignableFrom(type))
                {
                    foreach (MethodInfo method in type.GetMethods(Declared))
                    {
                        string kind = RpcKind(method);
                        if (kind == null) continue;
                        var sb = new StringBuilder();
                        sb.Append(kind).Append(' ').Append(Name(type)).Append('.').Append(method.Name).Append('(');
                        ParameterInfo[] parameters = method.GetParameters();
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(Name(parameters[i].ParameterType));
                        }
                        lines.Add(sb.Append(')').ToString());
                    }
                    foreach (FieldInfo field in type.GetFields(Declared))
                        if (field.FieldType.Namespace != null && field.FieldType.Namespace.StartsWith("FishNet.Object.Synchronizing", StringComparison.Ordinal))
                            lines.Add("sync " + Name(type) + "." + field.Name + ":" + Name(field.FieldType));
                }
                else if (type.IsValueType && typeof(IBroadcast).IsAssignableFrom(type))
                {
                    var sb = new StringBuilder("broadcast ").Append(Name(type)).Append('{');
                    // Declaration order (MetadataToken): GetFields promises no order, and the wire order is the declaration order.
                    FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                    Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
                    foreach (FieldInfo field in fields)
                        sb.Append(Name(field.FieldType)).Append(' ').Append(field.Name).Append(';');
                    lines.Add(sb.Append('}').ToString());
                }
            }
            lines.Add(RevisionLine); // 054 review: wire changes the reflection cannot see
            return lines;
        }

        private static string RpcKind(MethodInfo method)
        {
            foreach (object attribute in method.GetCustomAttributes(false))
            {
                string name = attribute.GetType().Name;
                foreach (string rpc in RpcAttributes)
                    if (name == rpc) return rpc.Substring(0, rpc.Length - "Attribute".Length);
            }
            return null;
        }

        /// <summary>Namespace, name and generic arguments, without assembly versions.</summary>
        public static string Name(Type type)
        {
            if (type == null) return "?";
            if (type.IsArray) return Name(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type.IsByRef) return Name(type.GetElementType()) + "&";
            string name = (type.IsNested ? Name(type.DeclaringType) + "+" : type.Namespace != null ? type.Namespace + "." : string.Empty) + type.Name;
            if (!type.IsGenericType) return name;
            var sb = new StringBuilder(name).Append('<');
            Type[] args = type.GetGenericArguments();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Name(args[i]));
            }
            return sb.Append('>').ToString();
        }
    }
}
