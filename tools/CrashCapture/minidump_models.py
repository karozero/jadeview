"""Binary layout models for the x86 minidump reader."""
from dataclasses import dataclass


@dataclass(frozen=True)
class MemoryRegion:
    address: int
    size: int
    payload: int


@dataclass(frozen=True)
class DumpModule:
    address: int
    size: int
    name: str


@dataclass(frozen=True)
class DumpThread:
    thread_id: int
    context_size: int
    context_offset: int
