using BenchmarkDotNet.Running;

// Benchmarks run against a real PostgreSQL (see Bench.ConnectionString) — an in-memory database
// would hide the round-trip cost that batching exists to remove.
//
//   docker run -d --name dp-postgres -e POSTGRES_PASSWORD=VeryStr0ngP@ssw0rd \
//     -e POSTGRES_DB=dapperpipeline -p 5433:5432 postgres:17
//
//   dotnet run -c Release --project benchmarks/DapperPipeline.Benchmarks -- --filter "*" --job Short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
