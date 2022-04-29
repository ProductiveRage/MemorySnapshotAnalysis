using System.Collections;
using System.Collections.Immutable;
using Microsoft.Diagnostics.Runtime;

namespace ClrMdTest
{
    public static class ObjectExtensions
    {
        public delegate bool CanHandle(Type type);
        public delegate IEnumerable<string>? Render(object value);

        public static void Dump(
            this object value,
            string title,
            ImmutableArray<(CanHandle CanHandle, Render GetLines)>? typeHandlers = null,
            Action<string>? writeTo = null)
        {
            Console.WriteLine($"= {title} ========----------------------");
            Console.WriteLine();
            foreach (var line in Dump(value, parents: Enumerable.Empty<object>(), typeHandlers ?? DefaultTypeHandlers) ?? Enumerable.Empty<string>())
            {
                (writeTo ?? Console.WriteLine)(line);
            }
        }

        public static readonly ImmutableArray<(CanHandle CanHandle, Render GetLines)> DefaultTypeHandlers =
            ImmutableArray<(CanHandle, Render)>.Empty
                .Add((
                    type => typeof(Type).IsAssignableFrom(type),
                    value => new[] { ((Type)value).FullName! }))
                .Add((
                    type => typeof(ClrRuntime).IsAssignableFrom(type),
                    value => null)) // There isn't a convenient Name-like property to display and we don't want to dig into all of the modules, threads, etc..
                .Add((
                    type => typeof(ClrAppDomain).IsAssignableFrom(type) || typeof(ClrModule).IsAssignableFrom(type),
                    value => new[] { value.ToString()! }))
                .Add((
                    type => typeof(ClrType).IsAssignableFrom(type),
                    value => new[] { ((ClrType)value).Name! }))
                .Add((
                    type => typeof(IEnumerable<ClrStackFrame>).IsAssignableFrom(type),
                    value => RenderStackTrace((IEnumerable<ClrStackFrame>)value)));

        private static IEnumerable<string>? Dump(object value, IEnumerable<object> parents, ImmutableArray<(CanHandle CanHandle, Render GetLines)> typeHandlers)
        {
            if (value is null)
            {
                return new[] { $"{GetIndentation(parents.Count())}null" };
            }

            if (parents.Contains(value))
            {
                return new[] { $"{GetIndentation(parents.Count())}*Circular Reference*" };
            }

            var type = value.GetType();
            foreach (var (canHandle, getLines) in typeHandlers)
            {
                if (canHandle(type))
                {
                    return getLines(value)?.Select(line => $"{GetIndentation(parents.Count())}{line}");
                }
            }
            
            // Apply primitive handling (unless there was a custom renderer in the typeHandlers list for the current type)
            if (type.IsPrimitive || type.IsEnum || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(string))
            {
                var displayValue = type == typeof(int) || type == typeof(uint) || type == typeof(short) || type == typeof(ushort) || type == typeof(long) || type == typeof(ulong)
                    ? type.GetMethod("ToString", new[] { typeof(string) })!.Invoke(value, new[] { "n0" })
                    : value.ToString();

                return new[] { $"{GetIndentation(parents.Count())}{displayValue}" };
            }

            var getEnumerator = type.GetInterfaces()
                .Select(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? i.GetMethod("GetEnumerator")
                    : null)
                .FirstOrDefault(t => t is not null);
            if (getEnumerator is not null)
            {
                var enumerator = (IEnumerator)getEnumerator.Invoke(value, null)!;
                var items = new List<IEnumerable<string>>();
                while (enumerator.MoveNext())
                {
                    var lines = Dump(enumerator.Current, parents.Append(value), typeHandlers);
                    if (lines is not null)
                    {
                        if (IsSingleLine(lines))
                        {
                            items.Add(new[] { $"{GetIndentation(parents.Count())}{lines.First().Trim()}" });
                        }
                        else
                        {
                            if (items.Any())
                            {
                                lines = lines.Prepend("");
                            }
                            items.Add(lines);
                        }
                    }
                }
                if (!items.Any())
                {
                    items.Add(new[] { $"{GetIndentation(parents.Count())}[]" });
                }
                return items.SelectMany(lines => lines);
            }

            var fieldsAndProperties = type
                .GetFields()
                .Select(field => (field.Name, Value: field.GetValue(value)))
                .Concat(
                    type
                        .GetProperties()
                        .Where(property => property.GetIndexParameters().Length == 0)
                        .Select(property => (property.Name, Value: property.GetValue(value))));
            if (!fieldsAndProperties.Any())
            {
                return new[] { GetIndentation(parents.Count()) + "{}" };
            }
            else
            {
                var items = new List<IEnumerable<string>>();
                foreach (var (name, memberValue) in fieldsAndProperties)
                {
                    var lines = Dump(memberValue, parents.Append(value), typeHandlers);
                    if (lines is not null)
                    {
                        if (IsSingleLine(lines))
                        {
                            items.Add(new[] { $"{GetIndentation(parents.Count())}{name}: {lines.First().Trim()}" });
                        }
                        else
                        {
                            items.Add(new[] { $"{GetIndentation(parents.Count())}{name}:" }.Concat(lines));
                        }
                    }
                }
                return items.SelectMany(lines => lines);
            }
        }

        private static bool IsSingleLine(IEnumerable<string> lines) => lines.Take(2).Count() == 1;

        private static string GetIndentation(int indentationLevel) => new(' ', indentationLevel * 2);

        private static IEnumerable<string> RenderStackTrace(IEnumerable<ClrStackFrame> stackTrace)
        {
            if (!stackTrace.Any())
            {
                return new[] { "None available" };
            }

            return stackTrace
                .Append(null) // Add a null entry to the end to ensure any leftover NumberOfUnknownFramesQueued value isn't discarded
                .Aggregate(
                    seed: new { NumberOfUnknownFramesQueued = 0, Lines = Enumerable.Empty<string>() },
                    func: (acc, frame) =>
                    {
                        if (frame is not null && frame.Method is null && frame.FrameName is null)
                        {
                            return new { NumberOfUnknownFramesQueued = acc.NumberOfUnknownFramesQueued + 1, acc.Lines };
                        }
                        var lines = acc.Lines;
                        if (acc.NumberOfUnknownFramesQueued > 0)
                        {
                            lines = lines.Append((acc.NumberOfUnknownFramesQueued > 1 ? $"{acc.NumberOfUnknownFramesQueued}x " : "") + "Unknown");
                        }
                        if (frame is not null)
                        {
                            lines = lines.Append($"{frame.Method?.Signature ?? frame.FrameName ?? "Unknown"} [{frame.Kind}]");
                        }
                        return new { NumberOfUnknownFramesQueued = 0, Lines = lines };
                    })
                .Lines;
        }
    }
}