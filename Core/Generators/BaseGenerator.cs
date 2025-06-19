using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;

namespace Core.Generators
{
    /// <summary>
    /// Represents an abstract base class for generating code from Bebop schemas.
    /// This class encapsulates the common functionalities needed for various code generators.
    /// </summary>
    public abstract class BaseGenerator
    {
        /// <summary>
        /// The Bebop schema from which the code is generated.
        /// </summary>
        protected BebopSchema Schema = default!;

        /// <summary>
        /// Configuration settings specific to the generator.
        /// </summary>
        protected GeneratorConfig Config = default!;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseGenerator"/> class with a given schema and configuration.
        /// </summary>
        /// <param name="schema">The Bebop schema used for code generation.</param>
        /// <param name="config">The generator-specific configuration settings.</param>
        protected BaseGenerator() { }

        /// <summary>
        /// Generates code based on the provided Bebop schema.
        /// </summary>
        /// <returns>A string containing the generated code.</returns>
        public abstract ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the alias of the code generator, which uniquely identifies it among other generators.
        /// </summary>
        public abstract string Alias { get; set; }

        /// <summary>
        /// Gets the friendly name of the code generator, which is used for display purposes.
        /// </summary>
        public abstract string Name { get; set; }
    }
}
