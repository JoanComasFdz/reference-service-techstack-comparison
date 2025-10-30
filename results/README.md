# Benchmark Results

This directory contains official benchmark results from various hardware configurations. Each subdirectory represents a unique hardware setup and contains the complete test comparison report.

## Directory Structure

```ascii
results/
├── intel-i9-13900k-64gb-nvme-ubuntu2404/
│   └── test-report-comparison-20251031_120000.md
├── amd-ryzen9-7950x-32gb-ssd-ubuntu2204/
│   └── test-report-comparison-20251101_093000.md
└── apple-m2-pro-16gb-ssd-macos14/
    └── test-report-comparison-20251102_154500.md
```

## Hardware Naming Convention

Directory names follow this format: `{cpu-model}-{ram-size}-{storage-type}-{os-version}`

**Examples:**

- `intel-i9-13900k-64gb-nvme-ubuntu2404`
- `amd-ryzen9-7950x-32gb-ssd-debian12`
- `apple-m2-pro-16gb-ssd-macos14`
- `intel-xeon-e5-2680v4-128gb-raid-centos8`

**Guidelines:**

- **CPU:** Use brand, model, and generation (e.g., `intel-i9-13900k`, `amd-ryzen7-5800x`)
- **RAM:** Total capacity (e.g., `16gb`, `64gb`, `128gb`)
- **Storage:** Type (e.g., `nvme`, `ssd`, `hdd`, `raid`)
- **OS:** Distribution and major version (e.g., `ubuntu2404`, `debian12`, `macos14`, `windows11-wsl2`)

## What's Included

Each hardware directory contains the comparison report automatically generated after executing [run.sh](../run.sh) or [run-all-tests.sh](../scripts/tools/testing/run-all-tests.sh).

## How to Read Results

### Key Metrics Explained

**Event Processing Throughput:**

- **Average (msg/s):** Mean messages processed per second
- **Peak (msg/s):** Maximum throughput achieved
- **Min (msg/s):** Minimum throughput observed
- **Std Dev:** Standard deviation (lower = more consistent)
- **CV%:** Coefficient of Variation (lower = more stable performance)

**API Throughput:**

- **Total Requests:** Completed HTTP requests
- **Calls/s:** Requests per second
- **Response Time (ms):** Average response latency

**Resource Usage:**

- **CPU (%):** Process-specific and system-wide utilization
- **Memory (MB):** RSS (Resident Set Size) - actual RAM used

**Medals:** 🥇 Gold, 🥈 Silver, 🥉 Bronze awarded to top 3 performers in each category

### Performance Variability

**Coefficient of Variation (CV%)** indicates performance stability:

- **< 5%:** Excellent stability (very consistent)
- **5-10%:** Good stability (acceptable variance)
- **10-20%:** Moderate stability (noticeable variance)
- **> 20%:** High variability (inconsistent performance)

## Contributing Your Results

Want to add benchmark results from your hardware? Follow these steps:

### 1. Run Official Benchmark

Use the master orchestration script with default parameters:

```bash
./run.sh
```

**Official Test Parameters:**

- Events: 50,000
- API Duration: 120 seconds
- Mode: Native GraalVM builds
- All 12 implementations tested

This will generate results in: `./test-results-{timestamp}/`

### 2. Create Hardware Directory

Create a descriptive directory name following the naming convention:

```bash
# Example for Intel i9-13900K, 64GB RAM, NVMe, Ubuntu 24.04
mkdir -p results/intel-i9-13900k-64gb-nvme-ubuntu2404
```

### 4. Copy Comparison Report

Copy the generated comparison report to your hardware directory:

```bash
# Keep the original timestamp in the filename
cp ./test-results-20251031_120000/test-report-comparison-20251031_120000.md \
   results/intel-i9-13900k-64gb-nvme-ubuntu2404/
```

### 5. Commit Results

```bash
git add results/
git commit -m "Add benchmark results for Intel i9-13900K system"
git push
```

## Comparing Results Across Hardware

When comparing results from different hardware:

1. **CPU Architecture:** Varies significantly (ARM vs x86, core count, clock speed)
2. **Memory:** Capacity and speed affect caching behavior
3. **Storage:** NVMe vs SSD vs HDD impacts database I/O
4. **Operating System:** Kernel differences can affect performance
5. **Virtualization:** WSL2, VMs, containers add overhead

Results from similar hardware provide the most meaningful comparisons.

## Test Reproducibility

For reproducible results:

- Close unnecessary applications
- Use consistent test parameters (via `./run.sh`)
- Run tests when system is idle
- Disconnect all netwrok connections once validation passes (WiFi, Ethernet)
- Run multiple times and look for consistent patterns

**Note:** Some performance variation between runs is normal due to:

- OS background processes
- Thermal throttling
- CPU frequency scaling
- Memory cache states

## Need Help?

If you encounter issues running benchmarks, check `./logs/` directory.

## License

All benchmark results are provided as-is for informational purposes. Results are specific to the tested hardware and may not represent performance on your system.
