using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace EquityBrief.Tests.Harness;

// The source files a set of methods runs through, read off the compiled code
// rather than off a list: every method each calls, constructs or takes a delegate
// to, followed through the state machines and closures the compiler writes, kept
// where it is the repository's own, and each mapped to the file its sequence
// points name in the build's symbols.
internal static class SourcesReached
{
    static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => (ushort)code.Value);

    static readonly Dictionary<Assembly, MetadataReaderProvider> Symbols = [];

    internal static IReadOnlyList<string> From(IEnumerable<MethodBase> entries)
    {
        var seen = new HashSet<MethodBase>();
        var pending = new Queue<MethodBase>(entries);
        var files = new SortedSet<string>(StringComparer.Ordinal);

        while (pending.TryDequeue(out var method))
        {
            if (!Ours(method.DeclaringType) || !seen.Add(method))
            {
                continue;
            }

            if (FileOf(method) is { } file)
            {
                files.Add(file);
            }

            if (method.GetCustomAttribute<StateMachineAttribute>() is { } machine)
            {
                foreach (var step in Methods(machine.StateMachineType))
                {
                    pending.Enqueue(step);
                }
            }

            foreach (var reached in Called(method))
            {
                pending.Enqueue(reached);
            }
        }

        return [.. files];
    }

    static bool Ours(Type? type) =>
        type?.Assembly.GetName().Name is { } name
        && name.StartsWith("EquityBrief.", StringComparison.Ordinal)
        && name != "EquityBrief.Tests";

    static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));

    static IEnumerable<MethodBase> Called(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();

        if (il is null)
        {
            yield break;
        }

        var typeArguments = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var at = 0; at < il.Length;)
        {
            ushort value = il[at++];

            if (value == 0xFE)
            {
                value = (ushort)(0xFE00 | il[at++]);
            }

            var code = OpCodesByValue[value];
            var operand = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, at) * 4),
                _ => 4,
            };

            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType or OperandType.InlineField)
            {
                // A compiler-written type is followed whole, being a state machine or a closure.
                switch (Resolved(method.Module, BitConverter.ToInt32(il, at), typeArguments, methodArguments))
                {
                    case MethodBase called:
                        yield return called;
                        break;
                    case Type type when Ours(type) && type.IsDefined(typeof(CompilerGeneratedAttribute)):
                        foreach (var inner in Methods(type))
                        {
                            yield return inner;
                        }

                        break;
                    case FieldInfo { DeclaringType: { } holder } when Ours(holder) && holder.IsDefined(typeof(CompilerGeneratedAttribute)):
                        foreach (var inner in Methods(holder))
                        {
                            yield return inner;
                        }

                        break;
                }
            }

            at += operand;
        }
    }

    // A token outside the generic context the method is read in resolves to nothing rather than stopping the walk.
    static MemberInfo? Resolved(Module module, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static string? FileOf(MethodBase method)
    {
        var assembly = method.DeclaringType!.Assembly;

        if (!Symbols.TryGetValue(assembly, out var provider))
        {
            provider = MetadataReaderProvider.FromPortablePdbImage(
                ImmutableArray.Create(File.ReadAllBytes(Path.ChangeExtension(assembly.Location, ".pdb"))));
            Symbols[assembly] = provider;
        }

        var reader = provider.GetMetadataReader();
        var information = reader.GetMethodDebugInformation(
            MetadataTokens.MethodDebugInformationHandle(method.MetadataToken & 0x00FFFFFF));

        // A method with no sequence points, being one the compiler wrote whole, is in no file.
        var document = information.Document.IsNil
            ? information.GetSequencePoints().Select(point => point.Document).FirstOrDefault(one => !one.IsNil)
            : information.Document;

        if (document.IsNil)
        {
            return null;
        }

        var path = reader.GetString(reader.GetDocument(document).Name).Replace('\\', '/');

        // From the repository's own `src/`, whatever root the build recorded.
        return path[path.IndexOf("src/EquityBrief.", StringComparison.Ordinal)..];
    }
}
