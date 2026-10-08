"""Word-level timestamps via forced alignment -- for text that's already known from a fast
transcription (see _openvino_backend.py), not for the transcription itself.

Uses language-specific wav2vec2 CTC models from jonatasgrosman (Apache-2.0), NOT
torchaudio's built-in MMS_FA bundle or the ctc-forced-aligner package's default model --
both rely on the same Meta MMS checkpoint under CC-BY-NC 4.0, not usable commercially. For
client work (this project is one), that would be a real licensing problem, not a software
bug -- deliberately avoided.
"""
import numpy as np

import _console as c

MODELS = {
    "de": "jonatasgrosman/wav2vec2-large-xlsr-53-german",
    "en": "jonatasgrosman/wav2vec2-large-xlsr-53-english",
}

_cache: dict[str, tuple] = {}


def is_supported(language: str) -> bool:
    return language in MODELS


def _load(language: str):
    if language not in _cache:
        from transformers import Wav2Vec2ForCTC, Wav2Vec2Processor

        model_id = MODELS[language]
        c.info(f"Loading alignment model for '{language}' ({model_id}) ...")
        processor = Wav2Vec2Processor.from_pretrained(model_id)
        model = Wav2Vec2ForCTC.from_pretrained(model_id)
        model.eval()
        _cache[language] = (model, processor)
        c.ok("Alignment model ready.")
    return _cache[language]


def align_words(samples: np.ndarray, text: str, language: str, sample_rate: int = 16000) -> list[dict]:
    """Returns a list of {start, end, text} per word (seconds, relative to the start of
    `samples`), or [] if the language isn't supported or alignment fails -- the caller then
    falls back to coarse segment boundaries."""
    if not is_supported(language) or not text.strip():
        return []

    try:
        import torch
        import torchaudio

        model, processor = _load(language)
        vocab = processor.tokenizer.get_vocab()
        # This model's CTC vocabulary is entirely lowercase (see vocab.json) --
        # text.upper() would make every character fall out of the vocab filter below and
        # turn every word into an empty string.
        delim_id = vocab.get(processor.tokenizer.word_delimiter_token or "|")

        # `words` is the lowercased, CTC-vocab-reduced form, used only for the alignment
        # itself -- `originals` keeps the original text's case and punctuation, filtered
        # in lockstep, so it can be used as the actual result text instead of the
        # cleaned-up alignment form.
        originals: list[str] = []
        words: list[str] = []
        for original in text.split():
            filtered = "".join(ch for ch in original.lower() if ch in vocab)
            if filtered:
                originals.append(original)
                words.append(filtered)
        if not words:
            return []

        waveform = torch.from_numpy(samples).unsqueeze(0)
        with torch.inference_mode():
            emissions = model(waveform).logits
        emissions = torch.log_softmax(emissions, dim=-1)

        # A word-delimiter token ("|") sits between words in the target sequence --
        # without it, two identical letters touching at a word boundary couldn't be told
        # apart, and the model has no slot for the pause it actually predicts at word
        # boundaries.
        expected_ids: list[int] = []
        word_slices: list[slice] = []
        for i, w in enumerate(words):
            if i > 0 and delim_id is not None:
                expected_ids.append(delim_id)
            start = len(expected_ids)
            expected_ids.extend(vocab[ch] for ch in w)
            word_slices.append(slice(start, len(expected_ids)))

        targets = torch.tensor([expected_ids], dtype=torch.int32)
        aligned, scores = torchaudio.functional.forced_align(
            emissions, targets, blank=vocab.get("<pad>", 0)
        )
        aligned = aligned[0]

        num_frames = emissions.size(1)
        seconds_per_frame = samples.shape[0] / sample_rate / num_frames

        spans = _merge_frames_to_spans(aligned.tolist(), blank=vocab.get("<pad>", 0))
        if len(spans) != len(expected_ids):
            # Tokenization and alignment output don't line up (e.g. characters in the
            # text the model doesn't know, or text/audio don't cover the same span) --
            # better to return nothing than mis-assigned timestamps.
            return []

        # Leading space, matching faster-whisper's word objects (see transcribe(),
        # word_timestamps=True) -- group_by_speaker() joins words without its own
        # separator ("".join) and relies on each word already carrying its own space.
        result = []
        for original, sl in zip(originals, word_slices):
            word_spans = spans[sl]
            result.append({
                "start": word_spans[0][0] * seconds_per_frame,
                "end": word_spans[-1][1] * seconds_per_frame,
                "text": " " + original,
            })
        return result
    except Exception as exc:
        c.warn(f"Forced alignment failed, falling back to segment boundaries: {exc}")
        return []


def _merge_frames_to_spans(aligned: list[int], blank: int) -> list[tuple[int, int]]:
    """Merges consecutive identical non-blank frames into (start, end) spans, end
    exclusive, in frame indices."""
    spans = []
    i, n = 0, len(aligned)
    while i < n:
        tok = aligned[i]
        if tok == blank:
            i += 1
            continue
        start = i
        while i < n and aligned[i] == tok:
            i += 1
        spans.append((start, i))
    return spans
