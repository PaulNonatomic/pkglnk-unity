using System;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Diagnostic menu items for inspecting NuGetForUnity's loaded
	/// assemblies and public API. Use to confirm the reflection
	/// heuristic in <see cref="NuGetPackageInstaller"/> hits a real
	/// method, or to figure out the actual API shape if it doesn't.
	/// Output goes to the Unity Console.
	/// </summary>
	public static class NuGetForUnityDiagnostics
	{
		[MenuItem("Tools/PkgLnk/Diagnostics/Probe NuGetForUnity")]
		private static void ProbeNuGetForUnity()
		{
			var sb = new StringBuilder();
			sb.AppendLine("[PkgLnk] NuGetForUnity probe:");
			sb.AppendLine();

			// 1. Find any loaded assembly with 'NugetForUnity' in the name.
			//    Casing is liberal because various forks have used different
			//    casing conventions.
			var nfuAssemblies = AppDomain.CurrentDomain
				.GetAssemblies()
				.Where(a => a.GetName().Name.IndexOf("nugetforunity", StringComparison.OrdinalIgnoreCase) >= 0)
				.ToArray();

			if (nfuAssemblies.Length == 0)
			{
				sb.AppendLine("  No assembly with 'NugetForUnity' in the name is loaded.");
				sb.AppendLine("  → NuGetForUnity is not installed (or its assemblies haven't compiled).");
				Debug.Log(sb.ToString());
				return;
			}

			sb.AppendLine($"  Loaded NuGetForUnity assemblies ({nfuAssemblies.Length}):");
			foreach (var asm in nfuAssemblies)
			{
				var name = asm.GetName();
				sb.AppendLine($"    {name.Name}  v{name.Version}");
			}
			sb.AppendLine();

			// 2. List types whose name contains "install" or "package".
			//    These are the candidates for housing the install API.
			sb.AppendLine("  Candidate types (name contains 'install' or 'package'):");
			foreach (var asm in nfuAssemblies)
			{
				var types = SafeGetTypes(asm);
				foreach (var type in types.OrderBy(t => t.FullName))
				{
					var lower = type.Name.ToLowerInvariant();
					if (!lower.Contains("install") && !lower.Contains("package")) continue;

					var isStatic = type.IsAbstract && type.IsSealed;
					sb.AppendLine($"    {type.FullName}  (public={type.IsPublic}, static={isStatic})");
				}
			}
			sb.AppendLine();

			// 3. For each PUBLIC type whose name contains "install",
			//    enumerate public-static methods. This is the search space
			//    NuGetPackageInstaller's reflection traverses.
			sb.AppendLine("  Public static methods on public types containing 'install':");
			var foundAny = false;
			foreach (var asm in nfuAssemblies)
			{
				var types = SafeGetTypes(asm);
				foreach (var type in types)
				{
					if (!type.IsPublic) continue;
					if (type.Name.IndexOf("install", StringComparison.OrdinalIgnoreCase) < 0) continue;

					var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
					if (methods.Length == 0) continue;

					sb.AppendLine($"    {type.FullName}:");
					foreach (var m in methods.OrderBy(m => m.Name))
					{
						var ps = m.GetParameters();
						var sig = string.Join(", ", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));
						sb.AppendLine($"      {m.Name}({sig}) -> {m.ReturnType.Name}");
						foundAny = true;
					}
				}
			}

			if (!foundAny)
			{
				sb.AppendLine("    (none found — install API may be on internal types or instance methods)");
			}
			sb.AppendLine();

			// 4. Specifically check the candidate types NuGetPackageInstaller
			//    looks for. This is the literal pass/fail list.
			sb.AppendLine("  NuGetPackageInstaller candidate-type lookup:");
			var candidates = new[]
			{
				"NugetForUnity.NugetPackageInstaller",
				"NugetForUnity.PackageInstaller",
				"NugetForUnity.NugetHelper"
			};

			foreach (var name in candidates)
			{
				Type found = null;
				foreach (var asm in nfuAssemblies)
				{
					found = asm.GetType(name, throwOnError: false, ignoreCase: false);
					if (found != null) break;
				}

				if (found == null)
				{
					sb.AppendLine($"    {name}  → NOT FOUND");
				}
				else
				{
					sb.AppendLine($"    {name}  → found in {found.Assembly.GetName().Name}");
				}
			}

			Debug.Log(sb.ToString());
		}

		private static Type[] SafeGetTypes(Assembly asm)
		{
			try
			{
				return asm.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types.Where(t => t != null).ToArray();
			}
		}
	}
}
