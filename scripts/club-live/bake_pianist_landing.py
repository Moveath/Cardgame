"""Bake the pianist's slow landing into frames, avoiding native decoder rate changes."""
import math
import numpy as np


def bake_landing(start, play, fps, entry):
    overlap = math.ceil(.22 * fps)
    phase = (entry - round(.22 * fps * .55)) % len(play)

    def sample(position):
        index = math.floor(position)
        weight = position - index
        return play[index % len(play)] * (1 - weight) + play[(index + 1) % len(play)] * weight

    result = list(start[:-overlap])
    for i, frame in enumerate(start[-overlap:]):
        t = (i + 1) / overlap
        alpha = t * t * (3 - 2 * t)
        result.append(frame * (1 - alpha) + sample(phase) * alpha)
        phase += .55
    ramp = math.ceil(.75 * fps)
    for i in range(ramp):
        result.append(sample(phase))
        t = (i + 1) / ramp
        phase += .55 + .45 * t * t * (3 - 2 * t)
    # End on exact loop frames so the runtime crossfade overlays identical poses.
    phase = math.ceil(phase)
    for i in range(overlap + 1):
        result.append(play[(phase + i) % len(play)])
    return result, (phase + overlap + 1) % len(play)
