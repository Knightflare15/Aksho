const emphasisOpen = '[[emphasis:';
const emphasisClose = ']]';

/**
 * Converts internal word-emphasis directives into punctuation Bulbul v3 can
 * interpret while preserving streaming across arbitrary LLM token boundaries.
 */
export class StreamingEmphasisAdapter {
  private buffer = '';
  private discardFollowingStop = false;
  readonly emphasizedTerms: string[] = [];

  constructor(private readonly conceptTerm?: string) {}

  push(fragment: string): string[] {
    if (!fragment) return [];
    this.buffer += fragment;
    return this.drain(false);
  }

  end(): string[] {
    const output = this.drain(true);
    this.buffer = '';
    return output;
  }

  private drain(final: boolean): string[] {
    const output: string[] = [];
    while (this.buffer) {
      if (this.discardFollowingStop) {
        if (/^[.!?।॥]/u.test(this.buffer)) this.buffer = this.buffer.replace(/^[.!?।॥]+/u, '');
        this.discardFollowingStop = false;
        if (!this.buffer) break;
      }
      const openingIndex = this.buffer.indexOf(emphasisOpen);
      if (openingIndex < 0) {
        const conceptIndex = this.findConceptTerm();
        if (conceptIndex >= 0) {
          if (conceptIndex > 0) output.push(this.buffer.slice(0, conceptIndex));
          const term = this.buffer.slice(conceptIndex, conceptIndex + this.conceptTerm!.length);
          this.emphasizedTerms.push(term);
          output.push(`${term}!`);
          this.discardFollowingStop = true;
          this.buffer = this.buffer.slice(conceptIndex + this.conceptTerm!.length);
          continue;
        }
        const safeLength = final
          ? this.buffer.length
          : this.buffer.length - Math.max(
            trailingDirectivePrefixLength(this.buffer),
            this.trailingConceptPrefixLength(),
          );
        if (safeLength <= 0) break;
        output.push(this.buffer.slice(0, safeLength));
        this.buffer = this.buffer.slice(safeLength);
        continue;
      }

      if (openingIndex > 0) {
        output.push(this.buffer.slice(0, openingIndex));
        this.buffer = this.buffer.slice(openingIndex);
        continue;
      }

      const closingIndex = this.buffer.indexOf(emphasisClose, emphasisOpen.length);
      if (closingIndex < 0) {
        if (!final) break;
        const incompleteTerm = this.buffer.slice(emphasisOpen.length).trim();
        if (incompleteTerm) output.push(incompleteTerm);
        this.buffer = '';
        break;
      }

      const term = this.buffer.slice(emphasisOpen.length, closingIndex).trim();
      this.buffer = this.buffer.slice(closingIndex + emphasisClose.length);
      if (!term) continue;
      this.emphasizedTerms.push(term);
      output.push(`${term}!`);
      this.discardFollowingStop = true;
    }
    return output;
  }

  private findConceptTerm(): number {
    if (!this.conceptTerm || this.emphasizedTerms.length > 0) return -1;
    const source = this.buffer.toLocaleLowerCase('en-IN');
    const target = this.conceptTerm.toLocaleLowerCase('en-IN');
    let index = source.indexOf(target);
    while (index >= 0) {
      const before = index > 0 ? this.buffer[index - 1]! : '';
      const after = this.buffer[index + this.conceptTerm.length] ?? '';
      if (!isWordCharacter(before) && !isWordCharacter(after)) return index;
      index = source.indexOf(target, index + 1);
    }
    return -1;
  }

  private trailingConceptPrefixLength(): number {
    if (!this.conceptTerm || this.emphasizedTerms.length > 0) return 0;
    const source = this.buffer.toLocaleLowerCase('en-IN');
    const target = this.conceptTerm.toLocaleLowerCase('en-IN');
    const maximum = Math.min(source.length, target.length - 1);
    for (let length = maximum; length > 0; length--) {
      if (source.endsWith(target.slice(0, length))) return length;
    }
    return 0;
  }
}

function trailingDirectivePrefixLength(value: string): number {
  const maximum = Math.min(value.length, emphasisOpen.length - 1);
  for (let length = maximum; length > 0; length--) {
    if (value.endsWith(emphasisOpen.slice(0, length))) return length;
  }
  return 0;
}

function isWordCharacter(value: string): boolean {
  return Boolean(value) && /[\p{L}\p{N}]/u.test(value);
}
