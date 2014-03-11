Streams.Interleaved
===================

Interleaved stream implementation. Enables multiple streams to be written and read as a sequential block chain.

Problem
=======

 * Given a set of unseekable streams, multiplex them to a single unseekable writable stream.
 * Given a single unseekable readable stream containing multiplexed streams, demux them as unseekable streams.
 * Each stream may be massively larger than available RAM.

Assumptions:
 * It is not necessary to support modification of the streams in-place. Once written they can only be read.
 * All involved streams are known at the start of the operation, ie. there is no need to support adding another stream to the set during the write. 
 * The process reading from the streams will expect them to yield data in approximately the same order it was written, relatively-speaking, ie. streams A, B and C are expected to cough up their parts of record X at the same time.
 
Possible Applications
=====================

 * Reshaping of row-wise data to column-wise, where the consumer is expecting to process all columns of a record at about the same time (original purpose, see below).
 * ReadyBoot-style caching for an application which must access many files during startup, but in a consistent pattern (may require some assembly).
 * Fast buffering (on rust) of many intermediate resultsets in a batch-processing system.
 * Basically any place where you need to buffer multiple parallel streams to a storage device which hates seeks AND your read pattern is similar to your write pattern.
 
Origins
=======

This started out as a thought experiment, where the goal was to reduce the size of SQL Server bulk export files. These compress only moderately well since they're made up of rows containing hetrogenous fields. Exporting column-wise theoretically yields a much higher compression ratio since similar types of data are kept together. Unfortunately data is read in rows from the database and must be written back in rows as well.
 * Buffering everything in memory is impractical once table size exceeds a few gigabytes.
 * Buffering on a rust-based block device imposes significant I/O overheads.
 * Writing multiple streams to different files may impose I/O overheads (depending on how the OS allocates blocks and the hardware schedules writes).
 * Reading multiple streams from different files certainly will impose I/O overheads, because our use case involved copying these files elsewhere and no matter how the OS allocated the blocks on write it certainly won't retain their relative positioning after a copy.
So the general problem to solve is that of turning multiple parallel writes into sequential I/O.

Other Stuff
===========

 * This project assumes .NET 4.5 because async streams look so fiiiiine (opinion subject to review once I've had to use them in anger).
 * This project will not use Reactive Extensions, because Rx is the best code-obfuscation-tool-disguised-as-useful-library I have encountered so far.

Some filesystems (notably ZFS) already offer improvements in handling of writes to spinning rust and with sufficient RAM can handle random reads swiftly too. Windows sadly lags some way behind Unixes in terms of filesystem development, but both can benefit from applications catering to the capabilities of the hardware rather than assuming that everyone has a solid-state disk. SSDs handle sequential writes well too, so there's no downside to optimising your disk access patterns for rust other than the maintenance overhead, and hopefully this library will be able to ease that in certain cases.

When I get around to it I plan on specifically implementing a wrapper to provide ReadyBoot-style caching capabilities. In the meantime, possibly someone could solve the eternally-hard problem of cache invalidation for me? >_>