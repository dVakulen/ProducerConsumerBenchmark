using System;
using BenchmarkDotNet.Running;

namespace ProducerConsumerBenchmark
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<Bench>();
            Console.Read();
        }
    }
}