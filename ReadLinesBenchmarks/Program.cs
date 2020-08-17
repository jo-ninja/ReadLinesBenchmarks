using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReadLinesBenchmarks
{
    [DisassemblyDiagnoser(printSource: true)]
    [MemoryDiagnoser]
    public class ReadLinesBenchmarks
    {
        private static ReadOnlySpan<byte> NewLine => new[] { (byte)'\r', (byte)'\n' };

        private Stream _stream;

        [Params(20/*, 300_000*/)]
        public int LineNumber { get; set; }

        [ParamsSource(nameof(LineCharMultiplierValues))]
        public int LineCharMultiplier { get; set; }
        public IEnumerable<int> LineCharMultiplierValues => new[] { 1, 2, 8, 1000 };
        //public IEnumerable<int> LineCharMultiplierValues => Enumerable.Range(1, 15).Concat(new[] { 20, 30, 50, 80, 100 });

        [GlobalSetup]
        public void GlobalSetup()
        {
            _stream = PrepareStream();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _stream.Dispose();
        }

        public Stream PrepareStream()
        {
            var stream = new MemoryStream();

            using var sw = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            foreach (var no in Enumerable.Range(1, LineNumber))
            {
                foreach (var _ in Enumerable.Range(1, LineCharMultiplier))
                {
                    sw.Write($"ABC{no:D7}");
                }
                sw.WriteLine();
            }
            sw.Flush();

            return stream;
        }

        [Benchmark(Baseline = true)]
        public async Task<string> ReadLineUsingStringReaderAsync()
        {
            _stream.Seek(0, SeekOrigin.Begin);

            var sr = new StreamReader(_stream, Encoding.UTF8);
            string str;
            while ((str = await sr.ReadLineAsync()) is not null)
            {
                // simulate string processing
                str = str.AsSpan().Slice(0, 5).ToString();
            }

            return str;
        }

        [Benchmark]
        public async Task<string> ReadLineUsingPipelineAsync()
        {
            _stream.Seek(0, SeekOrigin.Begin);

            var reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
            string str;
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                while ((str = ReadLine(ref buffer)) is not null)
                {
                    // simulate string processing
                    str = str.AsSpan().Slice(0, 5).ToString();
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }

            await reader.CompleteAsync();
            return str;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadLine(ref ReadOnlySequence<byte> buffer)
        {
            var reader = new SequenceReader<byte>(buffer);

            if (reader.TryReadTo(out var line, NewLine))
            {
                buffer = buffer.Slice(reader.Position);
                return Encoding.UTF8.GetString(line);
            }

            return default;
        }

        [Benchmark]
        public async Task<string> ReadLineUsingPipelineVer2Async()
        {
            _stream.Seek(0, SeekOrigin.Begin);

            var reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
            string str;

            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                str = ProcessLine(ref buffer);

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }

            await reader.CompleteAsync();
            return str;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ProcessLine(ref ReadOnlySequence<byte> buffer)
        {
            string str = null;

            if (buffer.IsSingleSegment)
            {
                var span = buffer.FirstSpan;
                int consumed;
                while (span.Length > 0)
                {
                    var newLine = span.IndexOf(NewLine);

                    if (newLine == -1) break;

                    var line = span.Slice(0, newLine);
                    str = Encoding.UTF8.GetString(line);

                    // simulate string processing
                    str = str.AsSpan().Slice(0, 5).ToString();

                    consumed = line.Length + NewLine.Length;
                    span = span.Slice(consumed);
                    buffer = buffer.Slice(consumed);
                }
            }
            else
            {
                var sequenceReader = new SequenceReader<byte>(buffer);

                while (!sequenceReader.End)
                {
                    while (sequenceReader.TryReadTo(out var line, NewLine))
                    {
                        str = Encoding.UTF8.GetString(line);

                        // simulate string processing
                        str = str.AsSpan().Slice(0, 5).ToString();
                    }

                    buffer = buffer.Slice(sequenceReader.Position);
                    sequenceReader.Advance(buffer.Length);
                }
            }

            return str;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            var summary = BenchmarkRunner.Run<ReadLinesBenchmarks>(new BenchmarkDotNet.Configs.DebugInProcessConfig());
#else
            var summary = BenchmarkRunner.Run<ReadLinesBenchmarks>();
#endif
        }
    }
}
