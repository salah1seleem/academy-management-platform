/** One-to-one digit conversion; preserves zeros, punctuation and exact value. */
export function canonicalDigits(value: string): string {
  return value.replace(/[٠-٩۰-۹]/g, digit => {
    const code = digit.charCodeAt(0);
    return String.fromCharCode(48 + code - (code >= 0x06f0 ? 0x06f0 : 0x0660));
  });
}

export function egyptianStaffMobile(value: string): string | null {
  const number = canonicalDigits(value).replace(/[\p{White_Space}()-]/gu, "");
  const match = /^(?:(?:\+|00)?200?|0)(1[0125][0-9]{8})$/.exec(number);
  return match ? `+20${match[1]}` : null;
}
