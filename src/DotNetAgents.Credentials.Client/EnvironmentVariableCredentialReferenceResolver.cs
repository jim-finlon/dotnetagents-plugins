using System.Text;
using DotNetAgents.Abstractions.PublicSubstitutes.Credentials;

namespace DotNetAgents.Credentials.Client;

/// <summary>
/// Local public substitute resolver that maps credential references to environment
/// variables by <c>{CATEGORY}__{NAME}__{VERSION}</c>, using upper snake case.
/// </summary>
public sealed class EnvironmentVariableCredentialReferenceResolver : ICredentialReferenceResolver
{
    private const string DefaultVersion = "default";
    private readonly Func<string, string?> _readEnvironmentVariable;

    /// <summary>Create a resolver backed by the current process environment.</summary>
    public EnvironmentVariableCredentialReferenceResolver()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    internal EnvironmentVariableCredentialReferenceResolver(Func<string, string?> readEnvironmentVariable)
    {
        _readEnvironmentVariable = readEnvironmentVariable ?? throw new ArgumentNullException(nameof(readEnvironmentVariable));
    }

    /// <inheritdoc />
    public ValueTask<ICredentialAccessor> ResolveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        var environmentVariableName = GetEnvironmentVariableName(reference);
        var value = _readEnvironmentVariable(environmentVariableName);
        if (value is null)
        {
            throw new KeyNotFoundException(
                $"Credential reference '{reference.Category}/{reference.Name}' was not found in environment variable '{environmentVariableName}'.");
        }

        return ValueTask.FromResult<ICredentialAccessor>(
            new EnvironmentVariableCredentialAccessor(reference, value.AsSpan().ToArray()));
    }

    /// <summary>Return the environment variable name used for the supplied reference.</summary>
    public static string GetEnvironmentVariableName(CredentialReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return string.Join("__",
            ToUpperSnake(reference.Category, nameof(reference.Category)),
            ToUpperSnake(reference.Name, nameof(reference.Name)),
            ToUpperSnake(string.IsNullOrWhiteSpace(reference.Version) ? DefaultVersion : reference.Version!, nameof(reference.Version)));
    }

    private static string ToUpperSnake(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpperInvariant(c));
                pendingSeparator = false;
                continue;
            }

            pendingSeparator = true;
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Credential reference component must contain at least one letter or digit.", parameterName);
        }

        return builder.ToString();
    }

    private sealed class EnvironmentVariableCredentialAccessor : ICredentialAccessor
    {
        private char[]? _buffer;

        public EnvironmentVariableCredentialAccessor(CredentialReference reference, char[] buffer)
        {
            Reference = reference;
            _buffer = buffer;
        }

        public CredentialReference Reference { get; }

        public ValueTask<SecretView> AccessAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return ValueTask.FromResult(new SecretView(_buffer.AsMemory()));
        }

        public ValueTask DisposeAsync()
        {
            var buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
            {
                Array.Clear(buffer);
            }

            return ValueTask.CompletedTask;
        }
    }
}
