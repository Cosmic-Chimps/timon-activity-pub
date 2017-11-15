import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

export class Emoji implements IComponent {
    private _tags: (string|AS.ASObject)[];

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this._process(element);
        this._tags = JSON.parse(element.dataset["emoji"]);
    }

    private _process(element: Node) {
        if (element.nodeType == Node.TEXT_NODE) {
        
        } else if (element.nodeType == Node.ELEMENT_NODE) {
            for (let child of Array.from(element.childNodes))
                this._process(child);
        }
    }

    unbind() {

    }
}
