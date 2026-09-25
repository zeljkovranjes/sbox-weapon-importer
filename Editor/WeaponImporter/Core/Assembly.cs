// Global usings for the s&box in-engine compiler, which injects no BCL usings.
// The plain net8 dev harness gets the same set from <ImplicitUsings>.
//
// Everything under Core/ is engine-agnostic (System.Numerics only) so it also builds in the
// offline test harness. s&box declares Vector3 in the global namespace, which shadows
// System.Numerics.Vector3, so every Core file that names Vector3 carries a namespace-scoped
// alias after its namespace line:
//
//     namespace WeaponImporter.Core.Xyz;
//     using Vector3 = System.Numerics.Vector3;

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
