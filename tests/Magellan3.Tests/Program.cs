using System;

namespace Magellan.Tests
{
    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine("Magellan 3 — Core logic test suite");
            Console.WriteLine("(pure half: no Decal, no game, no NuGet)");

            CoordsTests.Register();
            PlacesTests.Register();
            RendererTests.Register();
            OutlineTests.Register();
            DungeonAndConfigTests.Register();
            RoutingTests.Register();

            return T.Run();
        }
    }
}
