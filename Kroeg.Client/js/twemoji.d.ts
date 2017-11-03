export let base: string;

export let ext: string;

export let size: string;

export let className: string;

export namespace convert {
    export function fromCodePoint(hex: string): string;
    export function toCodePoint(surrogates: string, seperator?: string): string;
}

export type Options = {
  callback?: ReplacementFunction,
  base?: string,
  ext?: string,
  size?: string
};

export type ReplacementFunction = (iconId: string, options: Options, variant: string) => string;

export function parse(source: string, options?: Options): string;
export function parse(source: HTMLElement, options?: Options): HTMLElement;