using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Magellan.Tests
{
    /// <summary>
    /// A test harness with no package dependencies, deliberately.
    ///
    /// The plugin itself must build against .NET Framework 4.x on Windows, but the logic half is
    /// netstandard2.0 and has to be verifiable anywhere -- including in a Linux container with no
    /// NuGet feed. Swapping this for xUnit later is a one-line PackageReference; the assertions
    /// below don't change.
    /// </summary>
    public static class T
    {
        private static readonly List<(string name, Action body)> Tests = new List<(string, Action)>();
        private static string _suite = "";

        public static void Suite(string name) { _suite = name; }
        public static void Test(string name, Action body) { Tests.Add(($"{_suite} :: {name}", body)); }

        public static int Run()
        {
            int pass = 0;
            var failures = new List<string>();
            string suite = null;

            foreach (var (name, body) in Tests)
            {
                var s = name.Split(new[] { " :: " }, StringSplitOptions.None)[0];
                if (s != suite) { suite = s; Console.WriteLine(); Console.WriteLine("  " + suite); }

                var label = name.Substring(name.IndexOf(" :: ", StringComparison.Ordinal) + 4);
                try
                {
                    body();
                    pass++;
                    Console.WriteLine("    PASS  " + label);
                }
                catch (Exception ex)
                {
                    failures.Add(name + "\n          " + ex.Message);
                    Console.WriteLine("    FAIL  " + label);
                    Console.WriteLine("          " + ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine(new string('-', 72));
            Console.WriteLine($"  {pass}/{Tests.Count} passed" + (failures.Count > 0 ? $", {failures.Count} FAILED" : ""));
            Console.WriteLine(new string('-', 72));
            return failures.Count == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- assertions

        public static void True(bool cond, string msg)
        {
            if (!cond) throw new Exception("expected true: " + msg);
        }

        public static void False(bool cond, string msg) { True(!cond, msg); }

        public static void Eq<TV>(TV expected, TV actual, string msg)
        {
            if (!EqualityComparer<TV>.Default.Equals(expected, actual))
                throw new Exception($"{msg}: expected <{expected}>, got <{actual}>");
        }

        public static void Near(double expected, double actual, double tol, string msg)
        {
            if (Math.Abs(expected - actual) > tol)
                throw new Exception(string.Format(CultureInfo.InvariantCulture,
                    "{0}: expected {1:0.###} +/- {2:0.###}, got {3:0.###}  (delta {4:0.###})",
                    msg, expected, tol, actual, Math.Abs(expected - actual)));
        }

        public static void NotNull(object o, string msg)
        {
            if (o == null) throw new Exception("expected non-null: " + msg);
        }

        // ---------------------------------------------------------------- fixtures

        private static string _dataDir;

        /// <summary>Finds the repo's data/ directory by walking up from the test binary.</summary>
        public static string Data(string file)
        {
            if (_dataDir == null)
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "data")))
                    dir = dir.Parent;
                if (dir == null) throw new DirectoryNotFoundException("could not locate the repo's data/ directory");
                _dataDir = Path.Combine(dir.FullName, "data");
            }
            return Path.Combine(_dataDir, file);
        }
    }
}
