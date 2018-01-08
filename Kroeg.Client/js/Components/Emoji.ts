import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

export class Emoji implements IComponent {
    private _tags: string[];
    private _emojis: {name: string, elem: HTMLSpanElement}[] = [];
    private _resolved: {[name: string]: {item: AS.ASObject, icon: AS.ASObject}} = {};
    private _context: string;

    private static _emojiRegex: RegExp = /([^\w:]|\n|^):([a-zA-Z0-9_]{2,}):(?=[^\w:]|$)/;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this._context = element.dataset["context"];

        this._process(element);
        this._resolve();
    }

    private async _resolve(): Promise<void> {
        let entity = await this.entityStore.get(this._context);

        this._tags = AS.get(entity, 'tag');

        for (let tag of this._tags)
            this._resolveEmoji(tag);
    }

    private async _resolveEmoji(tag: string|AS.ASObject): Promise<void> {
        let emoji: AS.ASObject;
        if (typeof(tag) === 'string')
            emoji = await this.entityStore.get(tag as string);
        else
            emoji = tag as AS.ASObject;
        if (!AS.contains(emoji, 'type', 'Emoji')) return;
        let name = AS.take(emoji, 'name');
        let icon = typeof(AS.take(emoji, 'icon')) == "string" ? await this.entityStore.get(AS.take(emoji, 'icon')) : AS.take(emoji, 'icon');

        for (let emojo of this._emojis) {
            if (emojo.name == name) {
                let img = document.createElement("img");
                img.classList.add("emoji");
                img.alt = img.title = name;
                img.src = AS.take(icon, 'url');
                while (emojo.elem.firstChild) emojo.elem.removeChild(emojo.elem.firstChild);
                emojo.elem.appendChild(img);
            }
        }

        this._resolved[name] = {item: emoji, icon};
    }

    private _process(element: Node) {
        if (element.nodeType == Node.TEXT_NODE) {
            let t = element as Text;
            let match: RegExpExecArray;
            while (match = Emoji._emojiRegex.exec(t.nodeValue)) {
                let secondPart = t.splitText(match.index + match[1].length);
                t = secondPart.splitText(match[2].length + 2);
                
                let span = document.createElement("span");

                element.parentElement.removeChild(secondPart);
                element.parentElement.insertBefore(span, t);
                
                span.textContent = ":" + match[2] + ":";

                this._emojis.push({name: ":" + match[2] + ":", elem: span});
            }
        } else if (element.nodeType == Node.ELEMENT_NODE) {
            for (let child of Array.from(element.childNodes))
                this._process(child);
        }
    }

    unbind() {

    }
}
