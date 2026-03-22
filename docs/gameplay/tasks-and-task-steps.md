# Tasks And Task Steps

Main takeaway: the end-of-match screen counts task steps, not just fully completed tasks.

## Why the numbers look strange

In the reward/stat logic, the `TaskCompleted` category is bound to `TasksStepCount`, not to the number of fully completed tasks.

That means:

- a single task can contribute more than `+1` to the match stats
- the sum on the screen can be larger than the expected number of "tasks"
- this does not look like a percentage rounding bug

## Confirmed step counts

- `CleanTable` — `5`
- `Toilet` — `1`
- `Cooking` — `6`
- `Alchemy` — `2`
- `JugPlace` — `5`
- `JugMaking` — `1`

Practical example:

- a table task with five buttons contributes `+5` to the end-of-match task stat

## Working UI hypothesis

The end screen mixes different metrics:

- in one place it counts tasks
- in another it counts steps
- task points likely exist nearby as well

That is why the result visually looks like "the sum is larger than the total".
