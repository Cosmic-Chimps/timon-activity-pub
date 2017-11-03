import { EntityStore, StoreActivityToken } from "../EntityStore";
import * as AS from "../AS";
import { RenderHost } from "../RenderHost";
import { IComponent } from "../IComponent";


export class SubRenderhost implements IComponent {
    public static RenderhostMap: WeakMap<HTMLElement, SubRenderhost> = new WeakMap();
    private _id: string;
    private _template: string;
    private _renderHost: RenderHost;

    constructor(public host: RenderHost, private entityStore: EntityStore, public element: HTMLElement) {
        SubRenderhost.RenderhostMap.set(element, this);

        this._id = element.dataset["id"];
        this._template = element.dataset["template"];

        this._renderHost = new RenderHost(host.renderer, entityStore, this._id, this._template, element, host);
        console.log(`Registered subrenderhost at`, element);
    }

    public navigate(id: string) {
        this._id = id;
        console.log(id);
        this._renderHost.id = id;
    }

    unbind() {
        console.log(`Unbound subrenderhost at`, this.element);
        this._renderHost.deregister();
    }
}