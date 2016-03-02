using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Analyzers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Diagnostics;
using Helios.Concurrency;
using Orleans.Runtime;

namespace ProducerConsumerBenchmark
{

    class Config : ManualConfig
    {
        public Config()
        {
            Add(new Job
            {
                LaunchCount = 2,
                TargetCount = 5,
                WarmupCount = 2
            });
        }
    }

    [Config(typeof(Config))]
    public class Bench
    {
       [Params(10000, 1000000)]
        public int Repeats { get; set; }

        public int TotalRepeats { get; set; }

       [Params(0, 1000)]
        public int WorkLoad { get; set; }

        [Params(1, 2, 3, 4)]
        public int Producers { get; set; }

       [Params(1, 2, 3, 4)]
        public int Consumers { get; set; }

        CountdownEvent countdown;

        [Setup]
        public void DedicatedThreadPoolBenchSetup()
        {
           
            countdown = new CountdownEvent(Producers * Repeats);
        }

        [Benchmark]
        public void Queue_TakeFromAny()
        {
            BlockingCollectionsArrayBench(() => new ConcurrentQueue<string>());
        }

        [Benchmark]
        public void Blocking_Bag_Array_TakeFromAny()
        {
            BlockingCollectionsArrayBench(() => new ConcurrentBag<string>());
        }

        [Benchmark]
        public void BlockingQueue_Take()
        {
            BlockingCollection_TakeBench(() => new ConcurrentQueue<string>());
        }

        [Benchmark]
        public void BlockingBag_Take()
        {
            BlockingCollection_TakeBench(() => new ConcurrentBag<string>());
        }

        [Benchmark]
        public void BlockingCollection_GetConsumingEnumerable()
        {
            var b1 = new BlockingCollection<string>();
            StartThreads(Consumers, () =>
            {
                foreach (var item in b1.GetConsumingEnumerable())
                {
                    ConsumerThreadWorkItem();
                }
            });

            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    b1.Add("Q");
                });
            });

            countdown.Wait();
            b1.CompleteAdding();
        }

        [Benchmark]
        public void BufferBlock_Take()
        {
            var b1 = new BufferBlock<string>();
            StartThreads(Consumers, () =>
            {
                string item;
                while (!countdown.IsSet)
                {
                    if (b1.TryReceive(out item))
                    {
                        ConsumerThreadWorkItem();
                    }
                }
            });

            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    b1.Post("W");
                });
            });

            countdown.Wait();
            b1.Complete();
        }

        [Benchmark]
        public void DedicatedThreadPool()
        {
            var pool = new DedicatedThreadPool(new DedicatedThreadPoolSettings(Consumers));
            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    if (!pool.QueueUserWorkItem(ConsumerThreadWorkItem))
                    {
                        throw new Exception("Failed to enqueue message");
                    }
                });
            });


            countdown.Wait();
        }

       // [Benchmark]
        public void NaiveLockedList()
        {
            if (Repeats >= 1000000)
            {
                // takes too much time
                return;
            }

            var b1 = new RuntimeQueue<string>();
            StartThreads(Consumers, () =>
            {
                while (!countdown.IsSet)
                {
                    var item = b1.Take();
                    ConsumerThreadWorkItem();
                }
            });

            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    b1.Add("Q");
                });
            });

            countdown.Wait();
        }

      [Benchmark]
        public void ActionBlockBench()
        {
            var actionBlock = new ActionBlock<string>(s =>
            {
                ConsumerThreadWorkItem();
            }, new ExecutionDataflowBlockOptions
            {
                SingleProducerConstrained = Producers == 1,
                MaxDegreeOfParallelism = Consumers
            });

            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    actionBlock.Post("W");
                });
            });

            countdown.Wait();
        }

        public void BlockingCollectionsArrayBench(Func<IProducerConsumerCollection<string>> factFunc)
        {
            var b1 = new BlockingCollection<string>(factFunc());
            var b2 = new BlockingCollection<string>(factFunc());
            var queueArray = new[] { b1, b2 };
            StartThreads(Consumers, () =>
            {
                while (!countdown.IsSet)
                {
                    string item;
                    if (BlockingCollection<string>.TryTakeFromAny(queueArray, out item, TimeSpan.FromMilliseconds(50)) > -1)
                    {
                        ConsumerThreadWorkItem();
                    }
                }
            });

            StartThreads(Producers, () =>
            {
                for (var j = 0; j < Repeats; j++)
                {
                    queueArray[j % queueArray.Length].Add("Q");
                }
            });

            countdown.Wait();
        }
        
        public void BlockingCollection_TakeBench(Func<IProducerConsumerCollection<string>> factFunc)
        {
            var b1 = new BlockingCollection<string>(factFunc());
            var cts = new CancellationTokenSource();
            StartThreads(Consumers, () =>
            {
                while (!countdown.IsSet)
                {
                    try
                    {
                        var item = b1.Take(cts.Token);
                        ConsumerThreadWorkItem();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

            StartThreads(Producers, () =>
            {
                ProducerRepeatsCycle(() =>
                {
                    b1.Add("Q");
                });
            });

            countdown.Wait();
            cts.Cancel();
        }

        private void ConsumerThreadWorkItem()
        {
            DoWork();
            countdown.Signal();
        }

        private void ProducerRepeatsCycle(Action act)
        {
            for (int i = 0; i < Repeats; i++)
            {
                act();
            }
        }

        private void DoWork()
        {
            for (var j = 0; j < WorkLoad; j++)
            {
                double q = j % 123.0;
                double w = (q + 2) / 2;
                double e = w * 3;
                double t = e + w;
                double f = t;
            }
        }

        private List<Thread> StartThreads(int count, Action workItem, bool startImmediately = true)
        {
            var threads = new List<Thread>(count);
            for (int i = 0; i < count; i++)
            {
                threads.Add(new Thread(() =>
                {
                    workItem();
                }));
            }

            if (startImmediately)
                threads.ForEach(thread => thread.Start());

            return threads;
        }

        private async Task<int> DoWorkAsync()
        {
            await Task.Delay(10);
            DoWork();
            return 1;
        }
    }
}