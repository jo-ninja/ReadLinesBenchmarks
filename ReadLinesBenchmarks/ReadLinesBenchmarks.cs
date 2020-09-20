using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Benchmarks
{
    public class ReadLinesBenchmarks : Base
    {
        [Benchmark(Baseline = true)]
        public async Task<string> ReadLineUsingStringReaderAsync()
        {
            var sr = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            string str;
            while ((str = await sr.ReadLineAsync()) is not null)
            {
                // simulate string processing
                str = str.AsSpan().Slice(0, 5).ToString();
            }
            sr.Dispose();
            return str;
        }

        [Benchmark]
        public string ReadLineUsingStringReader()
        {
            var sr = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
            string str;
            while ((str = sr.ReadLine()) is not null)
            {
                // simulate string processing
                str = str.AsSpan().Slice(0, 5).ToString();
            }
            sr.Dispose();
            return str;
        }

        [Benchmark]
        public async Task<string> ReadLineUsingPipelineAsync()
        {
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

            if (reader.TryReadTo(out ReadOnlySequence<byte> line, NewLine))
            {
                buffer = buffer.Slice(reader.Position);
                return Encoding.UTF8.GetString(line);
            }

            return default;
        }

        [Benchmark]
        public async Task<string> ReadLineUsingPipelineVer2Async()
        {
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
                    while (sequenceReader.TryReadTo(out ReadOnlySequence<byte> line, NewLine))
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
}
