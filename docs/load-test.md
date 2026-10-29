# Load test

How much the composed stack takes, where it gives first, and what that says about scaling it.
Measured on 2026-10-29 with k6 against the real API, SQL Server, and worker; the numbers belong to one machine and are a shape to reason from, not a capacity promise.

## How it was run

```sh
docker compose up --build --wait
docker compose --profile load run --rm -e RATE=200 load          # RATE checkouts a second, for 1 minute
docker compose down -v                                           # the test writes real payments; this resets the demo data
```

The script is `scripts/load/payments.js` (k6 2.2.0, in its own container on the compose network, so it reaches the API at `http://api:8080`).
Each iteration is one checkout as a merchant's server makes it: create a payment, which the simulated acquirer authorizes after its 80 ms latency; read it back; capture every second one; and read the newest page of the list, as the dashboard does.
So a rate of `RATE` checkouts a second is about 3.5 `RATE` requests a second, and every create and capture also writes an outbox message that the worker turns into a signed webhook to the stack's receiver.
k6 holds the arrival rate constant (`constant-arrival-rate`), so a slow answer does not slow the load down; it signs in once, as the demo merchant.

The run is judged by thresholds a checkout can live with: fewer than 1% failed requests, and a p95 of 500 ms for create and capture, 200 ms for a read, and 300 ms for the list.

The machine: Windows 11 with Docker Desktop given 32 CPUs and 16 GB; the load generator, the API, the worker, SQL Server, and the receiver all share it, one container each, with nothing tuned.

## Results

| Checkouts a second | Requests a second | Create p95 | Capture p95 | Read p95 | List p95 | Failed | Webhook backlog at the end |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 20 | 70 | 120 ms | 111 ms | 2.6 ms | 2.0 ms | 0% | none |
| 50 | 174 | 128 ms | 117 ms | 2.4 ms | 1.8 ms | 0% | none |
| 100 | 348 | 132 ms | 120 ms | 2.5 ms | 1.9 ms | 0% | none |
| 200 | 697 | 142 ms | 133 ms | 2.7 ms | 2.2 ms | 0% | none |
| 400 | 1,394 | 161 ms | 149 ms | 4.4 ms | 3.5 ms | 0% | 10,424 deliveries, gone 15 s later |
| 600 | 2,090 | 294 ms | 266 ms | 50 ms | 33 ms | 0% | 15,032 events and 14,782 deliveries, growing |
| 600, two workers | 2,088 | 346 ms | 462 ms | 74 ms | 48 ms | 0% | about 7,400 deliveries, steady, gone 10 s later |

- Up to 200 a second nothing moves but the acquirer's fixed latency: create and capture are its 80 ms plus 30 to 60 ms of the API's own work, and the reads stay at 2 ms on the clustered index (`MerchantId, CreatedAt, Sequence`).
- From 400 a second the tail grows: p99 for create reaches 0.8 s, while the p95 holds.
- At 600 a second every threshold still passes, but SQL Server uses 6 to 7.5 cores and the reads' p95 jumps from 4 ms to 50 ms as they queue behind the writes.

## Where it gives first: the asynchronous path

The API kept every objective at 600 checkouts a second, but the webhooks fell behind.
One worker dispatched and delivered about 400 events a second while the API was under that load; at 600 the outbox and the delivery queue both grew for the whole minute, so a merchant's webhook arrived later and later.
With the load gone, the same worker cleared 13,800 deliveries in about 14 seconds, so it is not slow on its own: it shares SQL Server with the API, and every dispatch and delivery is a database round trip that waits its turn.

A second worker (started by hand with `docker compose run -d --no-deps worker`) took half of each batch, because both claim rows with `UPDLOCK, READPAST` ([ADR 003](decisions/003-async-workflow-on-a-transactional-outbox.md)): the outbox stayed under 1,100 waiting, the deliveries leveled off at about 7,000 instead of growing, and both cleared within 10 seconds of the end.
The API paid for it with a slower tail (capture's p95 from 266 ms to 462 ms), because the extra worker is extra load on the same database.

So the order of limits on one database is:

1. **The worker's throughput**, first; more worker instances fix it, and nothing in the code needs to change.
2. **SQL Server's CPU**, next: at about 2,000 requests a second it is the one resource every component shares.
   The reads would move to a readable secondary (Azure SQL's read scale-out) and the event traffic to Azure Service Bus, which the outbox relays to in the production design, so the database stops carrying the queue.

What these numbers do not say: the network between the services is a local bridge with no latency, the acquirer is simulated with a fixed delay, the dataset is small (the 280 seeded payments plus what the test writes), and the load generator shares the machine.
A capacity plan would rerun this against the Azure deployment, with the database at its production size and the dataset at a realistic one.
