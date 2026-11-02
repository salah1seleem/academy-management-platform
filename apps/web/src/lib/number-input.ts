/** One-to-one digit conversion; preserves zeros, punctuation and exact value. */
export function canonicalDigits(value: string): string {
  return value.replace(/[٠-٩۰-۹]/g, digit => {
    const code = digit.charCodeAt(0);
    return String.fromCharCode(48 + code - (code >= 0x06f0 ? 0x06f0 : 0x0660));
  });
}
