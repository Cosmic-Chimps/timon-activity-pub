import { EntityStore, NotifyToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";

export class Live implements IComponent {
    private _items: {id: string, renderer: RenderHost}[] = [];
    private _template: string;
    private _notifyToken: NotifyToken;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        this._template = element.dataset.template;
        this._notifyToken = this.entityStore.listenCollection(element.dataset.id, (a) => this._addItem(a));
    }

    private _addItem(id: string) {
        let renderer = new RenderHost(this.host.renderer, this.entityStore, id, this._template, null, this.host);
        this._items.push({id, renderer});
        let store = document.createElement("div");
        store.classList.add("list-group-item");
        store.appendChild(renderer.element);
        this.element.insertBefore(store, this.element.firstChild);
    }

    unbind() {
        for (let item of this._items) item.renderer.deregister();
        this.entityStore.unlisten(this._notifyToken);
    }
}
