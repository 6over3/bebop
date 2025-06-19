using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Chord.Common;
using Chord.Runtime;
using Core.Exceptions;
using Core.Generators;
using Core.Generators.C;
using Core.Generators.CPlusPlus;
using Core.Generators.CSharp;
using Core.Generators.Dart;
using Core.Generators.Python;
using Core.Generators.Rust;
using Core.Generators.TypeScript;
using Core.Logging;
using Core.Meta;
using Core.Meta.Decorators;
using Spectre.Console;

namespace Core;

public class CompilerHost : IDisposable
{
    private readonly FrozenDictionary<string, DecoratorDefinition> _decorators;
    private readonly FrozenDictionary<string, BaseGenerator> _generators;
    private readonly ExtensionRuntime? _extensionRuntime;
    private readonly EnvironmentVariableStore _environmentVariableStore;
    private CompilerHost(FrozenDictionary<string, DecoratorDefinition> decorators, FrozenDictionary<string, BaseGenerator> generators, EnvironmentVariableStore environmentVariableStore, ExtensionRuntime? extensionRuntime)
    {
        _decorators = decorators;
        _generators = generators;
        _extensionRuntime = extensionRuntime;
        _environmentVariableStore = environmentVariableStore;
    }

    public bool TryGetDecorator(string identifier, [NotNullWhen(true)] out DecoratorDefinition? decorator)
    {
        return _decorators.TryGetValue(identifier, out decorator);
    }

    public bool TryGetGenerator(string alias, [NotNullWhen(true)] out BaseGenerator? generator)
    {
        return _generators.TryGetValue(alias, out generator);
    }

    public EnvironmentVariableStore EnvironmentVariableStore => _environmentVariableStore;

    public IEnumerable<(string Alias, string Name)> Generators
    {
        get
        {
            foreach (var generator in _generators)
            {
                yield return (generator.Key, generator.Value.Name);
            }
        }
    }

    public IEnumerable<DecoratorDefinition> Decorators
    {
        get
        {
            foreach (var decorator in _decorators)
            {
                yield return decorator.Value;
            }
        }
    }

    public IEnumerable<(string Name, string Version)> Extensions
    {
        get
        {
            if (_extensionRuntime is not null)
            {
                foreach (var extension in _extensionRuntime.Extensions)
                {
                    yield return (extension.Name, extension.Version);
                }
            }
        }
    }

    public void Dispose()
    {
        _extensionRuntime?.Dispose();
    }

    public class CompilerHostBuilder
    {
        private static bool _isBuilt = false;
        private ExtensionRuntime? _extensionRuntime;
        private readonly Dictionary<string, DecoratorDefinition> _decorators;
        private readonly Dictionary<string, BaseGenerator> _generators;
        private readonly EnvironmentVariableStore _environmentVariableStore;

        private CompilerHostBuilder(string workingDirectory)
        {

            _decorators = [];
            _generators = [];
            _environmentVariableStore = new EnvironmentVariableStore(workingDirectory);
        }

        public static CompilerHostBuilder Create(string workingDirectory)
        {
            return new CompilerHostBuilder(workingDirectory);
        }

        public CompilerHost Build()
        {
            if (_isBuilt)
            {
                throw new CompilerException("A compiler host has already been built.");
            }
            _isBuilt = true;
            return new CompilerHost(_decorators.ToFrozenDictionary(), _generators.ToFrozenDictionary(), _environmentVariableStore, _extensionRuntime);
        }

        public CompilerHostBuilder WithDefaults()
        {
            return WithDefaultDecorators()
            .WithDefaultGenerators();
        }

        public CompilerHostBuilder WithExtensions(Dictionary<string, string> extensions)
        {
            _extensionRuntime = new ExtensionRuntime(DotEnv.Generated.Environment.Version, DiagnosticLogger.Instance.Out, DiagnosticLogger.Instance.Error);
            foreach (var kv in extensions)
            {
                Extension? extension;
                try
                {
                    extension = _extensionRuntime.LoadExtension(kv.Key, kv.Value);
                }
                catch (Exception e) when (e is ExtensionRuntimeException)
                {
                    throw new CompilerException("Extension runtime encountered a fatal exception", e);
                }
                catch (Exception e) when (e is ExtensionException)
                {
                    DiagnosticLogger.Instance.Error.MarkupLineInterpolated($"[maroon]Skipping extension: {e.Message}[/]");
                    continue;
                }
                if (extension.Decorators is { Count: > 0 })
                {
                    foreach (var decorator in extension.Decorators)
                    {
                        WithDecorator(() =>
                        {
                            var parameters = new List<Meta.Decorators.DecoratorParameter>();
                            if (decorator.Parameters is not null)
                            {
                                foreach (var parameter in decorator.Parameters)
                                {

                                    string? defaultValue = parameter.DefaultValue is null ? null : parameter.DefaultValue.ToString();
                                    DecoratorParameterValueValidator? validator = null;
                                    if (parameter.Validator is not null)
                                    {
                                        if (string.IsNullOrWhiteSpace(parameter.ValidationErrorReason))
                                        {
                                            throw new InvalidOperationException($"Decorator parameter {parameter.Identifier} has a validator but no validation error reason.");
                                        }
                                        validator = new DecoratorParameterValueValidator(parameter.Validator, parameter.ValidationErrorReason);
                                    }
                                    parameters.Add(new Meta.Decorators.DecoratorParameter(parameter.Identifier, parameter.Description, BaseTypeHelpers.FromTokenString(parameter.Type), parameter.Required, defaultValue, validator));
                                }
                            }
                            return new ContributedDecorator(decorator.Identifier, decorator.Description, (DecoratorTargets)decorator.Targets, decorator.AllowMultiple, parameters.ToArray());
                        });
                    }
                }

                if (extension.Contributions is ChordGenerator chordGenerator)
                {
                    WithGenerator(chordGenerator.Alias, new ContributedGenerator(extension));
                }
            }
            return this;
        }

        public CompilerHostBuilder WithDefaultGenerators()
        {
            return WithGenerator("cs", new CSharpGenerator())
                .WithGenerator("dart", new DartGenerator())
                .WithGenerator("rust", new RustGenerator())
                .WithGenerator("py", new PythonGenerator())
                .WithGenerator("ts", new TypeScriptGenerator())
                .WithGenerator("cpp", new CPlusPlusGenerator())
                .WithGenerator("c", new CGenerator());

        }

        public CompilerHostBuilder WithGenerator(string alias, BaseGenerator generator)
        {
            if (_generators.ContainsKey(alias))
            {
                throw new CompilerException($"unable to register '{alias}': already registered.");
            }
            _generators.Add(alias, generator);
            return this;
        }


        public CompilerHostBuilder WithDefaultDecorators()
        {
            return WithDecorator(new FlagsDecorator())
            .WithDecorator(new OpcodeDecorator())
            .WithDecorator(new DeprecatedDecorator())
            .WithDecorator(new DebugDecorator());
        }

        public CompilerHostBuilder WithDecorator(DecoratorDefinition decorator)
        {
            if (_decorators.ContainsKey(decorator.Identifier))
            {
                throw new CompilerException($"unable to register '{decorator.Identifier}': already registered.");
            }
            _decorators.Add(decorator.Identifier, decorator);
            return this;
        }

        public CompilerHostBuilder WithDecorator(Func<DecoratorDefinition> mapper)
        {
            var instance = mapper();
            if (instance is null)
            {
                throw new CompilerException("mapper returned null");
            }
            if (_decorators.ContainsKey(instance.Identifier))
            {
                throw new CompilerException($"unable to register '{instance.Identifier}': already registered.");
            }
            _decorators.Add(instance.Identifier, instance);
            return this;
        }
    }
}